namespace WUBT10Fix;

internal sealed record AudioPlaybackTestResult(
    bool Success,
    string Message,
    string? DeviceName = null,
    string? DeviceId = null);

internal static class AudioPlaybackTester
{
    internal static async Task<AudioPlaybackTestResult> ActivateSilentlyPreservedEndpointAsync(
        string pnpInstanceId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        const string pnpPrefix = "SWD\\MMDEVAPI\\";
        if (!pnpInstanceId.StartsWith(pnpPrefix, StringComparison.OrdinalIgnoreCase))
            return new(false, "The WU-BT10 endpoint ID is not a Windows audio endpoint.");

        var reconnect = AudioEndpointNative.RequestBluetoothAudioReconnect(pnpInstanceId);
        DiagnosticTrace.Write("audio", "audio-driver-reconnect-requested", new Dictionary<string, object?>
        {
            ["endpointId"] = pnpInstanceId,
            ["success"] = reconnect.Success,
            ["status"] = $"0x{reconnect.Status:X8}",
            ["filterDeviceId"] = reconnect.FilterDeviceId,
            ["message"] = reconnect.Message
        });
        if (!reconnect.Success)
            return new(false, reconnect.Message, "WU-BT10 AUDIO", pnpInstanceId);

        var activationTimeout = timeout ?? TimeSpan.FromSeconds(30);
        try
        {
            var deadline = DateTime.UtcNow + activationTimeout;
            var nextReconnectAttempt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= nextReconnectAttempt)
                {
                    var retry = AudioEndpointNative.RequestBluetoothAudioReconnect(pnpInstanceId);
                    DiagnosticTrace.Write("audio", "audio-driver-reconnect-retried", new Dictionary<string, object?>
                    {
                        ["endpointId"] = pnpInstanceId,
                        ["success"] = retry.Success,
                        ["status"] = $"0x{retry.Status:X8}",
                        ["filterDeviceId"] = retry.FilterDeviceId,
                        ["message"] = retry.Message
                    });
                    nextReconnectAttempt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                }

                _ = AudioEndpointNative.SetVolume(pnpInstanceId, RepairEngine.SafeVolume);
                if (AudioEndpointNative.IsActive(pnpInstanceId)
                    && BluetoothNative.FindWuBt10AudioDevices().Any(item => item.Connected)
                    && AudioEndpointNative.TryGetVolume(pnpInstanceId, out var volume)
                    && volume <= RepairEngine.SafeVolume + 0.005f)
                {
                    return new(
                        true,
                        "Windows connected WU-BT10 AUDIO through its audio driver and verified the endpoint at 20% volume.",
                        "WU-BT10 AUDIO",
                        pnpInstanceId);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }

            return new(
                false,
                $"Windows did not activate WU-BT10 AUDIO within {activationTimeout.TotalSeconds:0} seconds. " +
                "On the piano, hold FUNCTION and press C-sharp 1 to make Audio available, then try again. " +
                "No audible test sound was played.",
                "WU-BT10 AUDIO",
                pnpInstanceId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                : ex.Message;
            DiagnosticTrace.Write("audio", "silent-activation-exception", new Dictionary<string, object?>
            {
                ["exception"] = ex.ToString(),
                ["exceptionType"] = ex.GetType().FullName,
                ["hresult"] = $"0x{ex.HResult:X8}",
                ["message"] = message
            });
            return new(false, message, "WU-BT10 AUDIO", pnpInstanceId);
        }
    }

}
