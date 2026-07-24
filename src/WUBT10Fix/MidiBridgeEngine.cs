using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using System.Threading.Channels;

namespace WUBT10Fix;

internal enum MidiBridgeState
{
    Stopped,
    Connecting,
    Connected,
    Recovering,
    Stopping
}

internal sealed class MidiBridgeEngine : IAsyncDisposable
{
    private static readonly Guid MidiServiceUuid = new("03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
    private static readonly Guid MidiCharacteristicUuid = new("7772E5DB-3868-4112-A1A9-F2669D106BF3");

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _bleWriteLock = new(1, 1);
    private readonly BleMidiParser _parser = new();
    private readonly object _parserGate = new();
    private readonly bool _maintainConnection;
    private readonly Func<CancellationToken, Task>? _afterMaintainConnection;
    private WindowsMidiLoopback? _loopback;
    private WinMmMidiOutput? _midiOutput;
    private WinMmMidiInput? _midiInput;
    private Channel<byte[]>? _outboundMidi;
    private Task? _outboundPump;
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private GattDeviceService? _service;
    private GattCharacteristic? _characteristic;
    private CancellationTokenSource? _runCancellation;
    private long _recoveringGeneration;
    private long _runGeneration;
    private int _stopRequestCount;
    private int _packetCount;
    private int _messageCount;
    private int _outboundMessageCount;
    private int _outboundDropCount;
    private int _disposeState;
    private DateTimeOffset? _firstPacketAt;
    private DateTimeOffset? _lastPacketAt;

    internal event Action<string>? StatusChanged;
    internal event Action<int>? MessageReceived;
    internal event Action<int>? MessageSent;
    internal event Action<int>? PacketReceived;
    internal event Action<MidiBridgeState>? StateChanged;

    internal bool IsRunning { get; private set; }
    internal bool IsConnected { get; private set; }
    internal MidiBridgeState State { get; private set; } = MidiBridgeState.Stopped;

    internal MidiBridgeEngine(
        bool maintainConnection = false,
        Func<CancellationToken, Task>? afterMaintainConnection = null)
    {
        _maintainConnection = maintainConnection;
        _afterMaintainConnection = afterMaintainConnection;
        Trace(
            "engine-created",
            ("maintainConnection", _maintainConnection),
            ("hasAfterMaintainConnectionHandoff", _afterMaintainConnection is not null));
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Trace("start-requested", ("state", State), ("isRunning", IsRunning));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            if (Volatile.Read(ref _stopRequestCount) > 0)
                throw new InvalidOperationException("The MIDI bridge is still stopping. Try Connect again after it has stopped.");

            if (IsRunning)
            {
                Trace("start-ignored-already-running");
                return;
            }

            var generation = Interlocked.Increment(ref _runGeneration);
            Interlocked.Exchange(ref _recoveringGeneration, 0);
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            SetState(MidiBridgeState.Connecting);
            Trace("run-started", ("generation", generation));

            StatusChanged?.Invoke("Creating the Windows MIDI port…");
            _loopback = new WindowsMidiLoopback();
            _midiOutput = await Task.Run(_loopback.OpenOutput, _runCancellation.Token);
            _outboundMidi = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _midiInput = await Task.Run(
                () => WindowsMidiLoopback.OpenInput(OnWindowsMidiReceived),
                _runCancellation.Token);
            Trace("midi-output-opened", ("port", WindowsMidiLoopback.PortName));

            await ConnectWithRetryAsync(TimeSpan.FromMinutes(1), generation, _runCancellation.Token);
            _outboundPump = Task.Run(
                () => PumpOutboundMidiAsync(_outboundMidi.Reader, _runCancellation.Token),
                CancellationToken.None);
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

    private async Task ConnectWithRetryAsync(
        TimeSpan timeout,
        long generation,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        var attempt = 0;
        Trace("connect-retry-started", ("timeoutMs", timeout.TotalMilliseconds), ("generation", generation));

        while (DateTime.UtcNow < deadline)
        {
            EnsureCurrentRun(generation, cancellationToken);
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
                EnsureCurrentRun(generation, cancellationToken);
                Trace("scan-candidate-selected", ("attempt", attempt), ("address", BluetoothNative.FormatAddress(address)));
                StatusChanged?.Invoke($"Connecting to live WU-BT10 MIDI ({BluetoothNative.FormatAddress(address)}) — attempt {attempt}…");

                await ReleaseBluetoothAsync(unsubscribe: false);
                if (await TryConnectOnceAsync(address, generation, cancellationToken))
                {
                    EnsureCurrentRun(generation, cancellationToken);
                    _messageCount = 0;
                    _packetCount = 0;
                    _firstPacketAt = null;
                    _lastPacketAt = null;
                    lock (_parserGate)
                        _parser.Reset();
                    IsConnected = true;
                    SetState(MidiBridgeState.Connected);
                    Trace("connect-attempt-succeeded", ("attempt", attempt), ("address", BluetoothNative.FormatAddress(address)));
                    StatusChanged?.Invoke($"Connected — select “{WindowsMidiLoopback.PortName}” in Piano Marvel or your MIDI app.");
                    return;
                }

                lastError = new InvalidOperationException("Windows did not finish the BLE-MIDI GATT connection.");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested || !IsCurrentRun(generation))
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

    private async Task<bool> TryConnectOnceAsync(
        ulong address,
        long generation,
        CancellationToken cancellationToken)
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
            EnsureCurrentRun(generation, cancellationToken);
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
            EnsureCurrentRun(generation, cancellationToken);
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
            EnsureCurrentRun(generation, cancellationToken);

            if (_afterMaintainConnection is not null)
            {
                Trace("gatt-handoff-started");
                await _afterMaintainConnection(cancellationToken);
                EnsureCurrentRun(generation, cancellationToken);
                Trace("gatt-handoff-completed");
                await Task.Delay(350, cancellationToken);
                EnsureCurrentRun(generation, cancellationToken);
            }

            StatusChanged?.Invoke("Opening the BLE-MIDI service…");
            var serviceResult = await device.GetGattServicesForUuidAsync(MidiServiceUuid, BluetoothCacheMode.Uncached);
            EnsureCurrentRun(generation, cancellationToken);
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
            EnsureCurrentRun(generation, cancellationToken);
            Trace("gatt-service-access-completed", ("access", access));
            if (access != DeviceAccessStatus.Allowed)
                throw new InvalidOperationException($"Windows denied access to WU-BT10 MIDI ({access}).");

            var characteristics = await service.GetCharacteristicsForUuidAsync(
                MidiCharacteristicUuid,
                BluetoothCacheMode.Uncached);
            EnsureCurrentRun(generation, cancellationToken);
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

            // Android's platform BLE-MIDI service reads the characteristic
            // before enabling notifications. That ordering improved the
            // observed WU-BT10 connection sequence, but it is retained as a
            // tested interoperability measure rather than claimed as a
            // BLE-MIDI specification requirement.
            try
            {
                var initialRead = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                EnsureCurrentRun(generation, cancellationToken);
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
            EnsureCurrentRun(generation, cancellationToken);
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
            if (!ReferenceEquals(sender, _device))
            {
                Trace("stale-connection-status-ignored", ("connectionStatus", sender.ConnectionStatus));
                return;
            }

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

            var generation = Volatile.Read(ref _runGeneration);
            var cancellationToken = _runCancellation?.Token ?? CancellationToken.None;
            if (Interlocked.CompareExchange(ref _recoveringGeneration, generation, 0) == 0)
            {
                Trace("recovery-scheduled", ("generation", generation));
                _ = Task.Run(() => RecoverUntilConnectedAsync(generation, cancellationToken));
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
            var isCurrent = ReferenceEquals(sender, _session);
            Trace(
                "gatt-session-status-changed",
                ("sessionStatus", sender.SessionStatus),
                ("error", args.Error),
                ("isCurrent", isCurrent),
                ("isRunning", IsRunning),
                ("state", State));
        }
        catch (Exception ex)
        {
            RepairLog.Write("MIDI GATT session-status handler failed: " + ex);
        }
    }

    private async Task RecoverUntilConnectedAsync(long generation, CancellationToken cancellationToken)
    {
        Trace("recovery-started", ("generation", generation));
        try
        {
            await _lifecycle.WaitAsync(cancellationToken);
            try
            {
                EnsureCurrentRun(generation, cancellationToken);
                await ReleaseBluetoothAsync(unsubscribe: false);
                while (IsCurrentRun(generation) && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await ConnectWithRetryAsync(TimeSpan.FromMinutes(1), generation, cancellationToken);
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
            Interlocked.CompareExchange(ref _recoveringGeneration, 0, generation);
            Trace(
                "recovery-ended",
                ("generation", generation),
                ("isCurrent", IsCurrentRun(generation)),
                ("isRunning", IsRunning),
                ("isConnected", IsConnected),
                ("state", State));
        }
    }

    internal async Task StopAsync()
    {
        Trace("stop-requested", ("isRunning", IsRunning), ("state", State));
        Interlocked.Increment(ref _stopRequestCount);
        var invalidatedGeneration = Interlocked.Increment(ref _runGeneration);
        var wasRunning = IsRunning;
        IsRunning = false;
        IsConnected = false;
        if (wasRunning)
            SetState(MidiBridgeState.Stopping);
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
            Interlocked.Decrement(ref _stopRequestCount);
            Trace("stop-request-ended", ("invalidatedGeneration", invalidatedGeneration));
        }
    }

    private void OnMidiValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            if (!ReferenceEquals(sender, _characteristic) || !IsRunning)
            {
                Trace("stale-midi-packet-ignored", ("isRunning", IsRunning));
                return;
            }

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
            IReadOnlyList<byte[]> messages;
            lock (_parserGate)
                messages = _parser.Parse(bytes);
            foreach (var message in messages)
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

    private void OnWindowsMidiReceived(byte[] message)
    {
        var channel = _outboundMidi;
        if (channel is not null && channel.Writer.TryWrite(message))
            return;

        var dropped = Interlocked.Increment(ref _outboundDropCount);
        if (dropped == 1 || dropped % 100 == 0)
            Trace("outbound-midi-dropped", ("dropCount", dropped), ("reason", channel is null ? "bridge-not-running" : "queue-full"));
    }

    private async Task PumpOutboundMidiAsync(ChannelReader<byte[]> reader, CancellationToken cancellationToken)
    {
        Trace("outbound-midi-pump-started");
        try
        {
            await foreach (var message in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await SendMidiToBluetoothAsync(message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace("outbound-midi-send-failed", ("exception", ex.ToString()));
                    RepairLog.Write("MIDI send-to-piano error: " + ex);
                    StatusChanged?.Invoke("A MIDI message could not be sent to the piano. The bridge will keep running.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal bridge shutdown.
        }
        finally
        {
            Trace(
                "outbound-midi-pump-ended",
                ("messageCount", Volatile.Read(ref _outboundMessageCount)),
                ("dropCount", Volatile.Read(ref _outboundDropCount)));
        }
    }

    private async Task SendMidiToBluetoothAsync(byte[] message, CancellationToken cancellationToken)
    {
        if (message.Length == 0)
            return;

        await _bleWriteLock.WaitAsync(cancellationToken);
        try
        {
            var characteristic = _characteristic;
            var session = _session;
            if (characteristic is null || session is null || !IsConnected)
            {
                Trace("outbound-midi-dropped", ("reason", "ble-not-connected"));
                return;
            }

            var properties = characteristic.CharacteristicProperties;
            var writeOption = properties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithResponse
                : properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : throw new InvalidOperationException("The WU-BT10 MIDI characteristic is not writable.");
            var maximumPacketSize = Math.Clamp(checked((int)session.MaxPduSize) - 3, 20, 512);
            IReadOnlyList<byte[]> packets = message[0] == 0xF0
                ? BleMidiParser.EncodeSystemExclusive(message, maximumPacketSize)
                : [BleMidiParser.EncodeSingle(message)];

            foreach (var packet in packets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var writer = new DataWriter();
                writer.WriteBytes(packet);
                var buffer = writer.DetachBuffer();
                var result = await characteristic.WriteValueAsync(buffer, writeOption);
                if (result != GattCommunicationStatus.Success)
                    throw new InvalidOperationException($"WU-BT10 rejected a MIDI write ({result}).");
            }

            var count = Interlocked.Increment(ref _outboundMessageCount);
            MessageSent?.Invoke(count);
            if (count == 1 || count % 100 == 0)
            {
                Trace(
                    "outbound-midi-checkpoint",
                    ("messageCount", count),
                    ("byteCount", message.Length),
                    ("packetCount", packets.Count),
                    ("writeOption", writeOption));
            }
        }
        finally
        {
            _bleWriteLock.Release();
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
        lock (_parserGate)
            _parser.Reset();
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
        _midiInput?.Dispose();
        _midiInput = null;
        _outboundMidi?.Writer.TryComplete();
        if (_outboundPump is not null)
        {
            try
            {
                await _outboundPump;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
        _outboundPump = null;
        _outboundMidi = null;
        await ReleaseBluetoothAsync(unsubscribe: true);
        _midiOutput?.Dispose();
        _midiOutput = null;
        _loopback?.Dispose();
        _loopback = null;
        _runCancellation?.Dispose();
        _runCancellation = null;
        Interlocked.Exchange(ref _outboundMessageCount, 0);
        Interlocked.Exchange(ref _outboundDropCount, 0);
        SetState(MidiBridgeState.Stopped);
        Trace("stop-core-completed");
    }

    private void SetState(MidiBridgeState state)
    {
        var previous = State;
        if (previous == state)
            return;

        State = state;
        Trace("state-changed", ("previous", previous), ("current", state));
        StateChanged?.Invoke(state);
    }

    private bool IsCurrentRun(long generation) =>
        IsRunning && generation == Volatile.Read(ref _runGeneration);

    private void EnsureCurrentRun(long generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentRun(generation))
            throw new OperationCanceledException("The MIDI connection attempt was superseded by a newer lifecycle operation.", cancellationToken);
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
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        try
        {
            await StopAsync();
        }
        finally
        {
            _bleWriteLock.Dispose();
            _lifecycle.Dispose();
            Volatile.Write(ref _disposeState, 2);
        }
    }
}
