namespace WUBT10Fix;

internal sealed record AudioSwitchResult(bool Success, string Message, bool Connected)
{
    internal static AudioSwitchResult Failed(string message) => new(false, message, false);
}

internal static class AudioConnectionController
{
    internal static async Task<AudioSwitchResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        DiagnosticTrace.Write("audio", "direct-connect-started");
        await Task.Run(
            () => RepairEngine.TraceAudioTransitionSnapshot("connect-requested"),
            cancellationToken);
        using var mutationLease = AudioOperationLease.TryAcquire();
        if (!mutationLease.IsAcquired)
            return Trace(AudioSwitchResult.Failed("Another Audio connection or recovery action is already running."), "direct-connect-already-running");
        try
        {
            if (!RepairEngine.IsAudioEndpointActive())
            {
                var endpointIds = RepairEngine.AudioEndpointIds();
                if (endpointIds.Length == 0)
                {
                    var pairing = await AudioPairingService.EnsurePairedAsync(cancellationToken);
                    if (!pairing.Success)
                        return Trace(AudioSwitchResult.Failed(pairing.Message), "direct-connect-pairing-failed");

                    endpointIds = await WaitForPlaybackEndpointAsync(cancellationToken);
                }

                if (endpointIds.Length != 1)
                {
                    return Trace(
                        AudioSwitchResult.Failed(
                            endpointIds.Length == 0
                                ? "WU-BT10 AUDIO paired, but Windows did not create its playback endpoint within 30 seconds."
                                : $"Windows returned {endpointIds.Length} WU-BT10 playback endpoints; exactly one is required. No connection change was made."),
                        "direct-connect-preserved-endpoint-ambiguous");
                }

                var activation = await AudioPlaybackTester.ActivateSilentlyPreservedEndpointAsync(
                    endpointIds[0],
                    cancellationToken: cancellationToken);
                if (!activation.Success)
                    return Trace(AudioSwitchResult.Failed(activation.Message), "direct-connect-silent-activation-failed");
            }

            var safeVolume = RepairEngine.SetSafeVolume();
            if (!safeVolume.Success)
                return Trace(AudioSwitchResult.Failed(safeVolume.Message), "direct-connect-volume-failed");

            var connected = BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected);
            if (!connected)
                return Trace(AudioSwitchResult.Failed("The endpoint is active, but Windows no longer reports WU-BT10 AUDIO as connected."), "direct-connect-transport-lost");

            await Task.Run(
                () => RepairEngine.TraceAudioTransitionSnapshot("connect-completed"),
                cancellationToken);
            return Trace(
                new(true, "Connected silently on the preserved playback endpoint. Windows volume is verified at 20%.", true),
                "direct-connect-completed");
        }
        catch (OperationCanceledException)
        {
            return Trace(AudioSwitchResult.Failed("Audio connection was cancelled."), "direct-connect-cancelled");
        }
        catch (Exception ex)
        {
            RepairLog.Write("Direct Audio connection failed: " + ex);
            DiagnosticTrace.Write("audio", "direct-connect-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            return AudioSwitchResult.Failed(ex.Message);
        }
    }

    internal static async Task<AudioSwitchResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        DiagnosticTrace.Write("audio", "direct-disconnect-started");
        await Task.Run(
            () => RepairEngine.TraceAudioTransitionSnapshot("disconnect-requested"),
            cancellationToken);
        using var mutationLease = AudioOperationLease.TryAcquire();
        if (!mutationLease.IsAcquired)
            return Trace(AudioSwitchResult.Failed("Another Audio connection or recovery action is already running."), "direct-disconnect-already-running");

        try
        {
            var endpointIds = RepairEngine.AudioEndpointIds();
            if (endpointIds.Length != 1)
            {
                return Trace(
                    AudioSwitchResult.Failed($"Expected one preserved WU-BT10 playback endpoint, but Windows returned {endpointIds.Length}. No Bluetooth change was made."),
                    "direct-disconnect-preserved-endpoint-ambiguous");
            }

            var endpointId = endpointIds[0];
            var transportConnected = BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected);
            if (!AudioEndpointNative.IsActive(endpointId) && !transportConnected)
            {
                return Trace(
                    new(true, "WU-BT10 AUDIO is already disconnected.", false),
                    "direct-disconnect-already-disconnected");
            }

            var disconnect = AudioEndpointNative.RequestBluetoothAudioDisconnect(endpointId);
            DiagnosticTrace.Write("audio", "audio-driver-disconnect-requested", new Dictionary<string, object?>
            {
                ["endpointId"] = endpointId,
                ["success"] = disconnect.Success,
                ["status"] = $"0x{disconnect.Status:X8}",
                ["filterDeviceId"] = disconnect.FilterDeviceId,
                ["message"] = disconnect.Message
            });
            if (!disconnect.Success)
                return Trace(AudioSwitchResult.Failed(disconnect.Message), "direct-disconnect-driver-request-failed");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var endpointActive = AudioEndpointNative.IsActive(endpointId);
                transportConnected = BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected);
                if (!endpointActive && !transportConnected)
                {
                    await Task.Run(
                        () => RepairEngine.TraceAudioTransitionSnapshot("disconnect-completed"),
                        cancellationToken);
                    return Trace(
                        new(true, "Disconnected only the WU-BT10 Audio profile. Pairing and Bluetooth MIDI were left unchanged.", false),
                        "direct-disconnect-completed");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            await Task.Run(
                () => RepairEngine.TraceAudioTransitionSnapshot("disconnect-timeout"),
                cancellationToken);
            return Trace(
                AudioSwitchResult.Failed("Windows accepted the Audio disconnect request, but the WU-BT10 endpoint did not become inactive within 15 seconds."),
                "direct-disconnect-timeout");
        }
        catch (OperationCanceledException)
        {
            return Trace(AudioSwitchResult.Failed("Audio disconnection was cancelled."), "direct-disconnect-cancelled");
        }
        catch (Exception ex)
        {
            RepairLog.Write("Direct Audio disconnection failed: " + ex);
            DiagnosticTrace.Write("audio", "direct-disconnect-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            return AudioSwitchResult.Failed(ex.Message);
        }
    }

    private static AudioSwitchResult Trace(AudioSwitchResult result, string eventName)
    {
        DiagnosticTrace.Write("audio", eventName, new Dictionary<string, object?>
        {
            ["success"] = result.Success,
            ["connected"] = result.Connected,
            ["message"] = result.Message
        });
        return result;
    }

    private static async Task<string[]> WaitForPlaybackEndpointAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpointIds = RepairEngine.AudioEndpointIds();
            if (endpointIds.Length > 0)
            {
                DiagnosticTrace.Write("audio-pairing", "playback-endpoint-created", new Dictionary<string, object?>
                {
                    ["endpointCount"] = endpointIds.Length
                });
                return endpointIds;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        DiagnosticTrace.Write("audio-pairing", "playback-endpoint-timeout");
        return [];
    }
}
