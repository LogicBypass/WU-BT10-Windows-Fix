using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WUBT10Fix;

internal sealed record AudioPairingResult(bool Success, bool PairingPerformed, string Message)
{
    internal static AudioPairingResult Failed(string message) => new(false, false, message);
}

internal static class AudioPairingService
{
    internal const string DeviceName = "WU-BT10 AUDIO";

    internal static async Task<AudioPairingResult> EnsurePairedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nativeDevices = BluetoothNative.FindWuBt10AudioDevices();
        if (nativeDevices.Any(IsRememberedOrAuthenticated))
        {
            return new(
                true,
                false,
                "WU-BT10 AUDIO is already paired. Waiting for Windows to expose its playback endpoint.");
        }

        if (nativeDevices.Count > 1)
        {
            return AudioPairingResult.Failed(
                $"Windows found {nativeDevices.Count} devices named WU-BT10 AUDIO. Move other adapters out of range and try again.");
        }

        if (nativeDevices.Count == 1)
        {
            var nativeDevice = nativeDevices[0];
            DiagnosticTrace.Write("audio-pairing", "address-resolution-started", new Dictionary<string, object?>
            {
                ["address"] = BluetoothNative.FormatAddress(nativeDevice.Address)
            });

            using var bluetoothDevice = await BluetoothDevice.FromBluetoothAddressAsync(
                nativeDevice.Address);
            cancellationToken.ThrowIfCancellationRequested();
            if (bluetoothDevice is not null &&
                string.Equals(
                    bluetoothDevice.Name?.Trim(),
                    DeviceName,
                    StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticTrace.Write("audio-pairing", "address-resolution-completed", new Dictionary<string, object?>
                {
                    ["deviceId"] = bluetoothDevice.DeviceInformation.Id,
                    ["canPair"] = bluetoothDevice.DeviceInformation.Pairing.CanPair,
                    ["isPaired"] = bluetoothDevice.DeviceInformation.Pairing.IsPaired
                });
                return await PairDeviceAsync(
                    bluetoothDevice.DeviceInformation,
                    cancellationToken);
            }

            DiagnosticTrace.Write("audio-pairing", "address-resolution-failed");
        }

        DiagnosticTrace.Write("audio-pairing", "discovery-started");
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(false);
        var discovered = await DeviceInformation.FindAllAsync(
            selector,
            [],
            DeviceInformationKind.AssociationEndpoint);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = ExactNameMatches(discovered, device => device.Name);
        DiagnosticTrace.Write("audio-pairing", "discovery-completed", new Dictionary<string, object?>
        {
            ["discoveredCount"] = discovered.Count,
            ["exactMatchCount"] = matches.Count
        });

        if (matches.Count == 0)
        {
            return AudioPairingResult.Failed(
                "WU-BT10 AUDIO is not available for pairing. On the piano, hold FUNCTION and press C-sharp 1, then press Connect Audio again.");
        }

        if (matches.Count > 1)
        {
            return AudioPairingResult.Failed(
                $"Windows found {matches.Count} unpaired devices named WU-BT10 AUDIO. Move other adapters out of range and try again.");
        }

        return await PairDeviceAsync(matches[0], cancellationToken);
    }

    private static async Task<AudioPairingResult> PairDeviceAsync(
        DeviceInformation device,
        CancellationToken cancellationToken)
    {
        if (device.Pairing.IsPaired)
        {
            return new(
                true,
                false,
                "WU-BT10 AUDIO became paired during discovery. Waiting for its playback endpoint.");
        }

        if (!device.Pairing.CanPair)
        {
            return AudioPairingResult.Failed(
                "Windows can see WU-BT10 AUDIO, but it is not accepting a pairing request. Put the piano in Audio pairing mode and try again.");
        }

        DiagnosticTrace.Write("audio-pairing", "pair-requested", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id
        });
        var startedAt = DateTime.UtcNow;
        var result = await device.Pairing.PairAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var elapsed = DateTime.UtcNow - startedAt;
        if (ShouldRetryImmediateFailure(result.Status, elapsed))
        {
            DiagnosticTrace.Write("audio-pairing", "pair-immediate-failure-retry", new Dictionary<string, object?>
            {
                ["status"] = result.Status.ToString(),
                ["elapsedMs"] = elapsed.TotalMilliseconds
            });
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            startedAt = DateTime.UtcNow;
            result = await device.Pairing.PairAsync();
            cancellationToken.ThrowIfCancellationRequested();
            elapsed = DateTime.UtcNow - startedAt;
        }

        var success = IsSuccessfulStatus(result.Status);
        DiagnosticTrace.Write("audio-pairing", "pair-completed", new Dictionary<string, object?>
        {
            ["status"] = result.Status.ToString(),
            ["success"] = success,
            ["elapsedMs"] = elapsed.TotalMilliseconds
        });

        return success
            ? new(
                true,
                result.Status == DevicePairingResultStatus.Paired,
                "WU-BT10 AUDIO pairing completed. Waiting for Windows to create its playback endpoint.")
            : AudioPairingResult.Failed(PairingFailureMessage(result.Status));
    }

    internal static List<T> ExactNameMatches<T>(
        IEnumerable<T> candidates,
        Func<T, string?> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nameSelector);

        return candidates
            .Where(candidate => string.Equals(
                nameSelector(candidate)?.Trim(),
                DeviceName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    internal static bool IsSuccessfulStatus(DevicePairingResultStatus status) =>
        status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;

    internal static bool ShouldRetryImmediateFailure(
        DevicePairingResultStatus status,
        TimeSpan elapsed) =>
        status == DevicePairingResultStatus.Failed &&
        elapsed >= TimeSpan.Zero &&
        elapsed < TimeSpan.FromSeconds(2);

    internal static string PairingFailureMessage(DevicePairingResultStatus status) =>
        status switch
        {
            DevicePairingResultStatus.AuthenticationTimeout =>
                "WU-BT10 AUDIO pairing timed out. Put the piano in Audio pairing mode and try again.",
            DevicePairingResultStatus.AuthenticationFailure =>
                "Windows could not authenticate WU-BT10 AUDIO. Clear the Audio pairing record on both devices, then try again.",
            DevicePairingResultStatus.ConnectionRejected =>
                "WU-BT10 AUDIO rejected the pairing request. Put the piano in Audio pairing mode and try again.",
            DevicePairingResultStatus.NotReadyToPair =>
                "Windows Bluetooth is not ready. Check that Bluetooth is enabled, then try again.",
            DevicePairingResultStatus.TooManyConnections =>
                "WU-BT10 AUDIO cannot accept another connection. Disconnect it from another phone or computer, then try again.",
            DevicePairingResultStatus.OperationAlreadyInProgress =>
                "Another Bluetooth pairing operation is already running. Finish or cancel it, then try again.",
            _ => $"Windows could not pair WU-BT10 AUDIO ({status})."
        };

    private static bool IsRememberedOrAuthenticated(BluetoothNative.BluetoothDeviceInfo device) =>
        device.Remembered || device.Authenticated;
}
