using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace WUBT10Fix;

internal enum MidiBridgeState
{
    Stopped,
    Connecting,
    Connected,
    Recovering
}

internal sealed class MidiBridgeEngine : IAsyncDisposable
{
    private static readonly Guid MidiServiceUuid = new("03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
    private static readonly Guid MidiCharacteristicUuid = new("7772E5DB-3868-4112-A1A9-F2669D106BF3");

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly bool _maintainConnection;
    private WindowsMidiLoopback? _loopback;
    private WinMmMidiOutput? _midiOutput;
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private GattDeviceService? _service;
    private GattCharacteristic? _characteristic;
    private CancellationTokenSource? _runCancellation;
    private int _recoveryInProgress;
    private int _packetCount;
    private int _messageCount;
    private DateTimeOffset? _firstPacketAt;
    private DateTimeOffset? _lastPacketAt;

    internal event Action<string>? StatusChanged;
    internal event Action<int>? MessageReceived;
    internal event Action<int>? PacketReceived;
    internal event Action<MidiBridgeState>? StateChanged;

    internal bool IsRunning { get; private set; }
    internal bool IsConnected { get; private set; }
    internal MidiBridgeState State { get; private set; } = MidiBridgeState.Stopped;

    internal MidiBridgeEngine(bool maintainConnection = false)
    {
        _maintainConnection = maintainConnection;
        Trace("engine-created", ("maintainConnection", _maintainConnection));
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Trace("start-requested", ("state", State), ("isRunning", IsRunning));
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                Trace("start-ignored-already-running");
                return;
            }

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            SetState(MidiBridgeState.Connecting);

            StatusChanged?.Invoke("Creating the Windows MIDI port…");
            _loopback = new WindowsMidiLoopback();
            _midiOutput = await Task.Run(_loopback.OpenOutput, _runCancellation.Token);
            Trace("midi-output-opened", ("port", WindowsMidiLoopback.PortName));

            await ConnectWithRetryAsync(TimeSpan.FromMinutes(1), _runCancellation.Token);
        }
        catch (Exception ex)
        {
            Trace("start-failed", ("exception", ex.ToString()));
            await StopCoreAsync();
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task ConnectWithRetryAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        var attempt = 0;
        Trace("connect-retry-started", ("timeoutMs", timeout.TotalMilliseconds));

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                StatusChanged?.Invoke(attempt == 1
                    ? "Waiting for a live WU-BT10 MIDI signal…"
                    : $"Waiting for WU-BT10 MIDI before retry {attempt}…");

                var scanTime = deadline - DateTime.UtcNow;
                if (scanTime > TimeSpan.FromSeconds(15))
                    scanTime = TimeSpan.FromSeconds(15);
                if (scanTime <= TimeSpan.Zero)
                    break;

                Trace("scan-attempt-started", ("attempt", attempt), ("scanTimeoutMs", scanTime.TotalMilliseconds));
                var address = await FindAdvertisingWuBt10Async(scanTime, cancellationToken);
                Trace("scan-candidate-selected", ("attempt", attempt), ("address", BluetoothNative.FormatAddress(address)));
                StatusChanged?.Invoke($"Connecting to live WU-BT10 MIDI ({BluetoothNative.FormatAddress(address)}) — attempt {attempt}…");

                await ReleaseBluetoothAsync(unsubscribe: false);
                if (await TryConnectOnceAsync(address, cancellationToken))
                {
                    _messageCount = 0;
                    _packetCount = 0;
                    _firstPacketAt = null;
                    _lastPacketAt = null;
                    IsConnected = true;
                    SetState(MidiBridgeState.Connected);
                    Trace("connect-attempt-succeeded", ("attempt", attempt), ("address", BluetoothNative.FormatAddress(address)));
                    StatusChanged?.Invoke($"Connected — select “{WindowsMidiLoopback.PortName}” in Piano Marvel or your MIDI app.");
                    return;
                }

                lastError = new InvalidOperationException("Windows did not finish the BLE-MIDI GATT connection.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Trace("connect-attempt-failed", ("attempt", attempt), ("exception", ex.ToString()));
                RepairLog.Write($"MIDI connect attempt {attempt} failed: {ex}");
            }

            await ReleaseBluetoothAsync(unsubscribe: false);
            if (DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }

        Trace("connect-retry-timed-out", ("attempts", attempt), ("lastError", lastError?.ToString()));
        throw new TimeoutException(
            "WU-BT10 MIDI did not finish connecting within 60 seconds. Make sure the piano is advertising MIDI and no phone, tablet, or other MIDI app is connected.",
            lastError);
    }

    private async Task<bool> TryConnectOnceAsync(ulong address, CancellationToken cancellationToken)
    {
        BluetoothLEDevice? device = null;
        GattSession? session = null;
        GattDeviceService? service = null;
        GattCharacteristic? characteristic = null;
        var committed = false;
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            Trace("gatt-connect-started", ("address", BluetoothNative.FormatAddress(address)));
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device is null)
            {
                Trace("bluetooth-device-open-returned-null", ("address", BluetoothNative.FormatAddress(address)));
                return false;
            }

            Trace(
                "bluetooth-device-opened",
                ("address", BluetoothNative.FormatAddress(address)),
                ("name", device.Name),
                ("deviceId", device.BluetoothDeviceId.Id),
                ("connectionStatus", device.ConnectionStatus));

            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            if (session is null)
            {
                Trace("gatt-session-open-returned-null", ("deviceId", device.BluetoothDeviceId.Id));
                return false;
            }

            session.SessionStatusChanged += OnSessionStatusChanged;
            Trace(
                "gatt-session-opened",
                ("sessionStatus", session.SessionStatus),
                ("canMaintainConnection", session.CanMaintainConnection),
                ("maxPduSize", session.MaxPduSize));

            // Microsoft documents that this queues a real connection and waits
            // indefinitely for the peripheral to become available. Service
            // discovery below is the finite operation that tells us whether the
            // link and GATT database are ready yet.
            if (_maintainConnection)
            {
                session.MaintainConnection = true;
                Trace("gatt-maintain-connection-changed", ("value", true));
            }
            else
            {
                Trace("gatt-maintain-connection-skipped", ("reason", "finite-uncached-service-discovery-policy"));
            }
            await Task.Delay(350, cancellationToken);

            StatusChanged?.Invoke("Opening the BLE-MIDI service…");
            var serviceResult = await device.GetGattServicesForUuidAsync(MidiServiceUuid, BluetoothCacheMode.Uncached);
            Trace(
                "gatt-service-query-completed",
                ("status", serviceResult.Status),
                ("protocolError", serviceResult.ProtocolError),
                ("serviceCount", serviceResult.Services.Count));
            if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            {
                foreach (var unused in serviceResult.Services)
                    unused.Dispose();
                return false;
            }

            service = serviceResult.Services[0];
            foreach (var unused in serviceResult.Services.Skip(1))
                unused.Dispose();

            var access = await service.RequestAccessAsync();
            Trace("gatt-service-access-completed", ("access", access));
            if (access != DeviceAccessStatus.Allowed)
                throw new InvalidOperationException($"Windows denied access to WU-BT10 MIDI ({access}).");

            var characteristics = await service.GetCharacteristicsForUuidAsync(
                MidiCharacteristicUuid,
                BluetoothCacheMode.Uncached);
            Trace(
                "gatt-characteristic-query-completed",
                ("status", characteristics.Status),
                ("protocolError", characteristics.ProtocolError),
                ("characteristicCount", characteristics.Characteristics.Count));
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                return false;

            characteristic = characteristics.Characteristics[0];
            Trace(
                "gatt-characteristic-selected",
                ("uuid", characteristic.Uuid),
                ("properties", characteristic.CharacteristicProperties));

            // The BLE-MIDI specification requires the characteristic to be
            // read before notifications are enabled. Android's platform
            // BLE-MIDI service follows this sequence. Some peripherals may
            // still complete the read with a non-success status, so record the
            // outcome and continue to the notification step just as Android's
            // callback-based implementation does.
            try
            {
                var initialRead = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                Trace(
                    "gatt-characteristic-initial-read-completed",
                    ("status", initialRead.Status),
                    ("protocolError", initialRead.ProtocolError),
                    ("byteCount", initialRead.Value?.Length));
            }
            catch (Exception ex)
            {
                Trace("gatt-characteristic-initial-read-failed", ("exception", ex.ToString()));
            }

            characteristic.ValueChanged += OnMidiValueChanged;

            var subscription = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            Trace("gatt-cccd-subscribe-completed", ("status", subscription));
            if (subscription != GattCommunicationStatus.Success)
            {
                characteristic.ValueChanged -= OnMidiValueChanged;
                return false;
            }

            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                Trace("gatt-connect-rejected-device-not-connected", ("connectionStatus", device.ConnectionStatus));
                characteristic.ValueChanged -= OnMidiValueChanged;
                return false;
            }

            _device = device;
            _session = session;
            _service = service;
            _characteristic = characteristic;
            device.ConnectionStatusChanged += OnConnectionStatusChanged;
            committed = true;
            Trace(
                "gatt-connect-committed",
                ("elapsedMs", (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                ("connectionStatus", device.ConnectionStatus),
                ("sessionStatus", session.SessionStatus));
            return true;
        }
        finally
        {
            if (!committed)
            {
                Trace(
                    "gatt-connect-cleanup-started",
                    ("elapsedMs", (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                    ("hasDevice", device is not null),
                    ("hasSession", session is not null),
                    ("hasService", service is not null),
                    ("hasCharacteristic", characteristic is not null));
                if (characteristic is not null)
                    characteristic.ValueChanged -= OnMidiValueChanged;
                try
                {
                    if (session is not null)
                    {
                        session.MaintainConnection = false;
                        Trace("gatt-maintain-connection-changed", ("value", false), ("context", "failed-connect-cleanup"));
                    }
                }
                catch (Exception ex)
                {
                    Trace("gatt-maintain-connection-change-failed", ("context", "failed-connect-cleanup"), ("exception", ex.ToString()));
                    // A failed attempt may already have lost its GATT session.
                }

                if (session is not null)
                    session.SessionStatusChanged -= OnSessionStatusChanged;
                service?.Dispose();
                session?.Dispose();
                device?.Dispose();
                Trace("gatt-connect-cleanup-completed");
            }
        }
    }

    internal static async Task<ulong> FindAdvertisingWuBt10Async(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += OnAdvertisement;
        watcher.Stopped += OnWatcherStopped;

        void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var name = args.Advertisement.LocalName ?? string.Empty;
            var advertisesMidi = args.Advertisement.ServiceUuids.Contains(MidiServiceUuid);
            if (advertisesMidi || name.Contains("WU-BT10 MIDI", StringComparison.OrdinalIgnoreCase))
            {
                Trace(
                    "advertisement-candidate-received",
                    ("address", BluetoothNative.FormatAddress(args.BluetoothAddress)),
                    ("addressType", OptionalRuntimeProperty(args, "BluetoothAddressType")),
                    ("name", name),
                    ("advertisesMidi", advertisesMidi),
                    ("isConnectable", OptionalRuntimeProperty(args, "IsConnectable")),
                    ("advertisementType", args.AdvertisementType),
                    ("rssiDbm", args.RawSignalStrengthInDBm),
                    ("sourceTimestamp", args.Timestamp),
                    ("serviceUuids", string.Join(",", args.Advertisement.ServiceUuids)));
                found.TrySetResult(args.BluetoothAddress);
            }
        }

        void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Trace("advertisement-watcher-stopped", ("status", sender.Status), ("error", args.Error));
        }

        Trace("advertisement-watcher-starting", ("timeoutMs", timeout.TotalMilliseconds), ("scanningMode", watcher.ScanningMode));
        watcher.Start();
        Trace("advertisement-watcher-started", ("status", watcher.Status));
        try
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            using var registration = timeoutCancellation.Token.Register(
                () => found.TrySetCanceled(timeoutCancellation.Token));
            return await found.Task;
        }
        finally
        {
            Trace("advertisement-watcher-stop-requested", ("status", watcher.Status));
            watcher.Stop();
            watcher.Received -= OnAdvertisement;
            watcher.Stopped -= OnWatcherStopped;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        try
        {
            Trace(
                "bluetooth-device-connection-status-changed",
                ("connectionStatus", sender.ConnectionStatus),
                ("isRunning", IsRunning),
                ("state", State));
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected || !IsRunning)
                return;

            IsConnected = false;
            SetState(MidiBridgeState.Recovering);
            StatusChanged?.Invoke("MIDI connection was lost. Waiting for WU-BT10 to advertise, then reconnecting automatically…");

            if (Interlocked.CompareExchange(ref _recoveryInProgress, 1, 0) == 0)
            {
                Trace("recovery-scheduled");
                _ = Task.Run(RecoverUntilConnectedAsync);
            }
        }
        catch (Exception ex)
        {
            Trace("connection-status-handler-failed", ("exception", ex.ToString()));
            RepairLog.Write("MIDI connection-status handler failed: " + ex);
        }
    }

    private void OnSessionStatusChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
    {
        try
        {
            Trace(
                "gatt-session-status-changed",
                ("sessionStatus", sender.SessionStatus),
                ("error", args.Error),
                ("isRunning", IsRunning),
                ("state", State));
        }
        catch (Exception ex)
        {
            RepairLog.Write("MIDI GATT session-status handler failed: " + ex);
        }
    }

    private async Task RecoverUntilConnectedAsync()
    {
        var cancellationToken = _runCancellation?.Token ?? CancellationToken.None;
        Trace("recovery-started");
        try
        {
            await _lifecycle.WaitAsync(cancellationToken);
            try
            {
                await ReleaseBluetoothAsync(unsubscribe: false);
                while (IsRunning && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await ConnectWithRetryAsync(TimeSpan.FromMinutes(1), cancellationToken);
                        return;
                    }
                    catch (TimeoutException ex)
                    {
                        Trace("recovery-window-timed-out", ("exception", ex.ToString()));
                        RepairLog.Write("MIDI recovery is still waiting: " + ex.Message);
                        StatusChanged?.Invoke("WU-BT10 MIDI is still unavailable. Automatic reconnect is continuing…");
                    }
                }
            }
            finally
            {
                _lifecycle.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Trace("recovery-cancelled");
            // An explicit Stop cancels discovery/recovery immediately.
        }
        catch (Exception ex)
        {
            Trace("recovery-failed", ("exception", ex.ToString()));
            RepairLog.Write("MIDI automatic recovery failed: " + ex);
            StatusChanged?.Invoke("Automatic MIDI reconnect hit an error and will retry when you press Connect.");
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryInProgress, 0);
            Trace("recovery-ended", ("isRunning", IsRunning), ("isConnected", IsConnected), ("state", State));
        }
    }

    internal async Task StopAsync()
    {
        Trace("stop-requested", ("isRunning", IsRunning), ("state", State));
        _runCancellation?.Cancel();
        await _lifecycle.WaitAsync();
        try
        {
            if (!IsRunning && State == MidiBridgeState.Stopped)
            {
                Trace("stop-ignored-already-stopped");
                return;
            }

            await StopCoreAsync();
            Trace("stop-completed");
            StatusChanged?.Invoke("MIDI bridge stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void OnMidiValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[checked((int)reader.UnconsumedBufferLength)];
            reader.ReadBytes(bytes);
            var packetCount = Interlocked.Increment(ref _packetCount);
            var receivedAt = DateTimeOffset.UtcNow;
            _firstPacketAt ??= receivedAt;
            _lastPacketAt = receivedAt;
            if (packetCount == 1 || packetCount % 100 == 0)
            {
                Trace(
                    packetCount == 1 ? "first-midi-packet-received" : "midi-packet-checkpoint",
                    ("packetCount", packetCount),
                    ("byteCount", bytes.Length),
                    ("messageCountBeforePacket", Volatile.Read(ref _messageCount)));
            }
            PacketReceived?.Invoke(packetCount);
            foreach (var message in BleMidiParser.ParseShortMessages(bytes))
            {
                _midiOutput?.Send(message);
                var count = Interlocked.Increment(ref _messageCount);
                MessageReceived?.Invoke(count);
            }
        }
        catch (Exception ex)
        {
            Trace("midi-forwarding-failed", ("exception", ex.ToString()));
            RepairLog.Write("MIDI forwarding error: " + ex);
            StatusChanged?.Invoke("A MIDI message could not be forwarded. View the repair log.");
        }
    }

    private async Task ReleaseBluetoothAsync(bool unsubscribe)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var device = _device;
        var session = _session;
        var service = _service;
        var characteristic = _characteristic;

        Trace(
            "gatt-release-started",
            ("unsubscribeRequested", unsubscribe),
            ("hasDevice", device is not null),
            ("hasSession", session is not null),
            ("hasService", service is not null),
            ("hasCharacteristic", characteristic is not null),
            ("deviceConnectionStatus", SafeDeviceStatus(device)),
            ("sessionStatus", SafeSessionStatus(session)),
            ("packetCount", Volatile.Read(ref _packetCount)),
            ("messageCount", Volatile.Read(ref _messageCount)),
            ("firstPacketAt", _firstPacketAt),
            ("lastPacketAt", _lastPacketAt));

        _device = null;
        _session = null;
        _service = null;
        _characteristic = null;
        IsConnected = false;

        if (device is not null)
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        if (characteristic is not null)
        {
            characteristic.ValueChanged -= OnMidiValueChanged;
            if (unsubscribe && device?.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                try
                {
                    var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                    Trace("gatt-cccd-unsubscribe-completed", ("status", result));
                }
                catch (Exception ex)
                {
                    Trace("gatt-cccd-unsubscribe-failed", ("exception", ex.ToString()));
                    // Power-off and radio loss make an explicit unsubscribe impossible.
                }
            }
            else if (unsubscribe)
            {
                Trace(
                    "gatt-cccd-unsubscribe-skipped",
                    ("reason", characteristic is null ? "no-characteristic" : "device-not-connected"),
                    ("deviceConnectionStatus", SafeDeviceStatus(device)));
            }
        }

        try
        {
            if (session is not null)
            {
                session.MaintainConnection = false;
                Trace(
                    "gatt-maintain-connection-changed",
                    ("value", false),
                    ("context", "release"),
                    ("sessionStatus", SafeSessionStatus(session)));
            }
        }
        catch (Exception ex)
        {
            Trace("gatt-maintain-connection-change-failed", ("context", "release"), ("exception", ex.ToString()));
            // Windows may already have invalidated the session.
        }

        service?.Dispose();
        if (session is not null)
            session.SessionStatusChanged -= OnSessionStatusChanged;
        session?.Dispose();
        device?.Dispose();
        Trace(
            "gatt-release-completed",
            ("elapsedMs", (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
            ("unsubscribeRequested", unsubscribe));
    }

    private async Task StopCoreAsync()
    {
        Trace("stop-core-started");
        IsRunning = false;
        _runCancellation?.Cancel();
        await ReleaseBluetoothAsync(unsubscribe: true);
        _midiOutput?.Dispose();
        _midiOutput = null;
        _loopback?.Dispose();
        _loopback = null;
        _runCancellation?.Dispose();
        _runCancellation = null;
        Interlocked.Exchange(ref _recoveryInProgress, 0);
        SetState(MidiBridgeState.Stopped);
        Trace("stop-core-completed");
    }

    private void SetState(MidiBridgeState state)
    {
        var previous = State;
        State = state;
        Trace("state-changed", ("previous", previous), ("current", state));
        StateChanged?.Invoke(state);
    }

    private static object? SafeDeviceStatus(BluetoothLEDevice? device)
    {
        try
        {
            return device?.ConnectionStatus;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static object? SafeSessionStatus(GattSession? session)
    {
        try
        {
            return session?.SessionStatus;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static string OptionalRuntimeProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType().GetProperty(propertyName)?.GetValue(source)?.ToString() ?? "not-exposed-by-contract";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static void Trace(string eventName, params (string Name, object? Value)[] data)
    {
        DiagnosticTrace.Write(
            "midi",
            eventName,
            data.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }
}
