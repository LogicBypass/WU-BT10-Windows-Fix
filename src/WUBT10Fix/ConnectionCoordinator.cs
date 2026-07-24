namespace WUBT10Fix;

internal enum ConnectionMode
{
    None,
    Audio,
    Midi
}

internal sealed record ConnectionActionResult(ConnectionMode ActiveMode, string Message);

internal sealed class ConnectionCoordinator : IDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Func<bool> _isAudioConnected;
    private readonly Func<bool> _isMidiRunning;
    private readonly Func<CancellationToken, Task<AudioSwitchResult>> _connectAudio;
    private readonly Func<CancellationToken, Task<AudioSwitchResult>> _disconnectAudio;
    private readonly Func<CancellationToken, Task> _startMidi;
    private readonly Func<Task> _stopMidi;
    private readonly Func<CancellationToken, Task> _waitForMidiAdvertisement;

    internal ConnectionCoordinator(
        Func<bool> isAudioConnected,
        Func<bool> isMidiRunning,
        Func<CancellationToken, Task<AudioSwitchResult>> connectAudio,
        Func<CancellationToken, Task<AudioSwitchResult>> disconnectAudio,
        Func<CancellationToken, Task> startMidi,
        Func<Task> stopMidi,
        Func<CancellationToken, Task> waitForMidiAdvertisement)
    {
        _isAudioConnected = isAudioConnected;
        _isMidiRunning = isMidiRunning;
        _connectAudio = connectAudio;
        _disconnectAudio = disconnectAudio;
        _startMidi = startMidi;
        _stopMidi = stopMidi;
        _waitForMidiAdvertisement = waitForMidiAdvertisement;
    }

    internal async Task<ConnectionActionResult> ConnectAudioAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            DiagnosticTrace.Write("connection", "audio-connect-requested", State());
            if (_isMidiRunning())
            {
                DiagnosticTrace.Write("connection", "midi-release-before-audio-started");
                await _stopMidi();
                await _waitForMidiAdvertisement(cancellationToken);
                if (_isMidiRunning())
                    throw new InvalidOperationException("MIDI did not stop cleanly, so Audio was not connected.");
                DiagnosticTrace.Write("connection", "midi-release-before-audio-completed");
            }

            var result = await _connectAudio(cancellationToken);
            EnsureAudioSucceeded(result);
            if (!_isAudioConnected())
                throw new InvalidOperationException("Windows completed the Audio request but did not report WU-BT10 AUDIO as connected.");
            if (_isMidiRunning())
                throw new InvalidOperationException("Audio connected while MIDI was still running. Disconnect both modes and try again.");

            DiagnosticTrace.Write("connection", "audio-connect-completed", State());
            return new(ConnectionMode.Audio, result.Message);
        }
        finally
        {
            _operations.Release();
        }
    }

    internal async Task<ConnectionActionResult> DisconnectAudioAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            DiagnosticTrace.Write("connection", "audio-disconnect-requested", State());
            var result = await _disconnectAudio(cancellationToken);
            EnsureAudioSucceeded(result);
            if (_isAudioConnected())
                throw new InvalidOperationException("Windows completed the Audio disconnect request but still reports WU-BT10 AUDIO as connected.");

            DiagnosticTrace.Write("connection", "audio-disconnect-completed", State());
            return new(ConnectionMode.None, result.Message);
        }
        finally
        {
            _operations.Release();
        }
    }

    internal async Task<ConnectionActionResult> ConnectMidiAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            DiagnosticTrace.Write("connection", "midi-connect-requested", State());
            if (!_isMidiRunning())
                await _startMidi(cancellationToken);

            if (!_isMidiRunning())
                throw new InvalidOperationException("The MIDI bridge did not remain running after its connection attempt.");
            if (_isAudioConnected())
            {
                await _stopMidi();
                throw new InvalidOperationException(
                    "WU-BT10 AUDIO remained connected, so MIDI was released to avoid unstable simultaneous operation.");
            }

            DiagnosticTrace.Write("connection", "midi-connect-completed", State());
            return new(
                ConnectionMode.Midi,
                $"Connected. Select “{WindowsMidiLoopback.PortName}” in your MIDI application.");
        }
        finally
        {
            _operations.Release();
        }
    }

    internal async Task<ConnectionActionResult> DisconnectMidiAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            DiagnosticTrace.Write("connection", "midi-disconnect-requested", State());
            if (_isMidiRunning())
                await _stopMidi();
            if (_isMidiRunning())
                throw new InvalidOperationException("The MIDI bridge did not stop cleanly.");

            await _waitForMidiAdvertisement(cancellationToken);
            DiagnosticTrace.Write("connection", "midi-disconnect-completed", State());
            return new(ConnectionMode.None, "MIDI disconnected cleanly and WU-BT10 is available for another connection.");
        }
        finally
        {
            _operations.Release();
        }
    }

    // Called by MidiBridgeEngine after it has created a GATT session and asked
    // Windows to retain the BLE connection, but before it discovers services.
    // ConnectMidiAsync already owns _operations, so this callback must not
    // acquire the same semaphore.
    internal async Task ReleaseAudioForMidiHandoffAsync(CancellationToken cancellationToken)
    {
        if (!_isAudioConnected())
        {
            DiagnosticTrace.Write("connection", "audio-release-before-midi-skipped", State());
            return;
        }

        DiagnosticTrace.Write("connection", "audio-release-before-midi-started", State());
        var result = await _disconnectAudio(cancellationToken);
        EnsureAudioSucceeded(result);
        if (_isAudioConnected())
            throw new InvalidOperationException("WU-BT10 AUDIO did not fully disconnect, so MIDI was not connected.");
        DiagnosticTrace.Write("connection", "audio-release-before-midi-completed", State());
    }

    private static void EnsureAudioSucceeded(AudioSwitchResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException(result.Message);
    }

    private Dictionary<string, object?> State() => new()
    {
        ["audioConnected"] = _isAudioConnected(),
        ["midiRunning"] = _isMidiRunning()
    };

    public void Dispose() => _operations.Dispose();
}
