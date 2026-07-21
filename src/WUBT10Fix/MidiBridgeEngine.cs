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

    internal event Action<string>? StatusChanged;
    internal event Action<int>? MessageReceived;
    internal event Action<int>? PacketReceived;
    internal event Action<MidiBridgeState>? StateChanged;

    internal bool IsRunning { get; private set; }
    internal bool IsConnected { get; private set; }
    internal MidiBridgeState State { get; private set; } = MidiBridgeState.Stopped;

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            SetState(MidiBridgeState.Connecting);

            StatusChanged?.Invoke("Creating the Windows MIDI port…");
            _loopback = new WindowsMidiLoopback();
            _midiOutput = await Task.Run(_loopback.OpenOutput, _runCancellation.Token);

            await ConnectWithRetryAsync(TimeSpan.FromMinutes(1), _runCancellation.Token);
        }
        catch
        {
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

                var address = await FindAdvertisingWuBt10Async(scanTime, cancellationToken);
                StatusChanged?.Invoke($"Connecting to live WU-BT10 MIDI ({BluetoothNative.FormatAddress(address)}) — attempt {attempt}…");

                await ReleaseBluetoothAsync(unsubscribe: false);
                if (await TryConnectOnceAsync(address, cancellationToken))
                {
                    _messageCount = 0;
                    _packetCount = 0;
                    IsConnected = true;
                    SetState(MidiBridgeState.Connected);
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
                RepairLog.Write($"MIDI connect attempt {attempt} failed: {ex}");
            }

            await ReleaseBluetoothAsync(unsubscribe: false);
            if (DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }

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

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device is null)
                return false;

            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            if (session is null)
                return false;

            // Microsoft documents that this queues a real connection and waits
            // indefinitely for the peripheral to become available. Service
            // discovery below is the finite operation that tells us whether the
            // link and GATT database are ready yet.
            session.MaintainConnection = true;
            await Task.Delay(350, cancellationToken);

            StatusChanged?.Invoke("Opening the BLE-MIDI service…");
            var serviceResult = await device.GetGattServicesForUuidAsync(MidiServiceUuid, BluetoothCacheMode.Uncached);
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
            if (access != DeviceAccessStatus.Allowed)
                throw new InvalidOperationException($"Windows denied access to WU-BT10 MIDI ({access}).");

            var characteristics = await service.GetCharacteristicsForUuidAsync(
                MidiCharacteristicUuid,
                BluetoothCacheMode.Uncached);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                return false;

            characteristic = characteristics.Characteristics[0];
            characteristic.ValueChanged += OnMidiValueChanged;

            var subscription = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (subscription != GattCommunicationStatus.Success)
            {
                characteristic.ValueChanged -= OnMidiValueChanged;
                return false;
            }

            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                characteristic.ValueChanged -= OnMidiValueChanged;
                return false;
            }

            _device = device;
            _session = session;
            _service = service;
            _characteristic = characteristic;
            device.ConnectionStatusChanged += OnConnectionStatusChanged;
            committed = true;
            return true;
        }
        finally
        {
            if (!committed)
            {
                if (characteristic is not null)
                    characteristic.ValueChanged -= OnMidiValueChanged;
                try
                {
                    if (session is not null)
                        session.MaintainConnection = false;
                }
                catch
                {
                    // A failed attempt may already have lost its GATT session.
                }

                service?.Dispose();
                session?.Dispose();
                device?.Dispose();
            }
        }
    }

    private static async Task<ulong> FindAdvertisingWuBt10Async(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += OnAdvertisement;

        void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var name = args.Advertisement.LocalName ?? string.Empty;
            var advertisesMidi = args.Advertisement.ServiceUuids.Contains(MidiServiceUuid);
            if (advertisesMidi || name.Contains("WU-BT10 MIDI", StringComparison.OrdinalIgnoreCase))
                found.TrySetResult(args.BluetoothAddress);
        }

        watcher.Start();
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
            watcher.Stop();
            watcher.Received -= OnAdvertisement;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        try
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected || !IsRunning)
                return;

            IsConnected = false;
            SetState(MidiBridgeState.Recovering);
            StatusChanged?.Invoke("MIDI connection was lost. Waiting for WU-BT10 to advertise, then reconnecting automatically…");

            if (Interlocked.CompareExchange(ref _recoveryInProgress, 1, 0) == 0)
                _ = Task.Run(RecoverUntilConnectedAsync);
        }
        catch (Exception ex)
        {
            RepairLog.Write("MIDI connection-status handler failed: " + ex);
        }
    }

    private async Task RecoverUntilConnectedAsync()
    {
        var cancellationToken = _runCancellation?.Token ?? CancellationToken.None;
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
            // An explicit Stop cancels discovery/recovery immediately.
        }
        catch (Exception ex)
        {
            RepairLog.Write("MIDI automatic recovery failed: " + ex);
            StatusChanged?.Invoke("Automatic MIDI reconnect hit an error and will retry when you press Connect.");
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryInProgress, 0);
        }
    }

    internal async Task StopAsync()
    {
        _runCancellation?.Cancel();
        await _lifecycle.WaitAsync();
        try
        {
            if (!IsRunning && State == MidiBridgeState.Stopped)
                return;

            await StopCoreAsync();
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
            RepairLog.Write("MIDI forwarding error: " + ex);
            StatusChanged?.Invoke("A MIDI message could not be forwarded. View the repair log.");
        }
    }

    private async Task ReleaseBluetoothAsync(bool unsubscribe)
    {
        var device = _device;
        var session = _session;
        var service = _service;
        var characteristic = _characteristic;

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
                    await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch
                {
                    // Power-off and radio loss make an explicit unsubscribe impossible.
                }
            }
        }

        try
        {
            if (session is not null)
                session.MaintainConnection = false;
        }
        catch
        {
            // Windows may already have invalidated the session.
        }

        service?.Dispose();
        session?.Dispose();
        device?.Dispose();
    }

    private async Task StopCoreAsync()
    {
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
    }

    private void SetState(MidiBridgeState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }
}
