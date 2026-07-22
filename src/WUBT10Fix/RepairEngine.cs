using System.Security.Principal;

namespace WUBT10Fix;

internal static class RepairEngine
{
    internal const float SafeVolume = 0.20f;
    private const string RegisteredEndpointQuery = "Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | Where-Object FriendlyName -like '*WU-BT10 AUDIO*' | ForEach-Object InstanceId";
    private const string DisabledBluetoothAudioQuery = "$d=Get-PnpDevice -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'INTELAUDIO*' -and $_.FriendlyName -like '*Bluetooth*Audio*' -and ($_.Problem -eq 22 -or [string]$_.Problem -eq 'CM_PROB_DISABLED') }; $d | ForEach-Object InstanceId";
    private const string HealthyIntelBluetoothAudioQuery = "$d=Get-PnpDevice -PresentOnly -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'INTELAUDIO*' -and $_.FriendlyName -like '*Bluetooth*Audio*' -and ($_.Problem -eq 0 -or [string]$_.Problem -eq 'CM_PROB_NONE') }; $d | ForEach-Object InstanceId";
    private const string HealthyWuA2dpQuery = "$d=Get-PnpDevice -PresentOnly -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'BTHENUM\\{0000110B-0000-1000-8000-00805F9B34FB}*' -and $_.FriendlyName -eq 'WU-BT10 AUDIO' -and ($_.Problem -eq 0 -or [string]$_.Problem -eq 'CM_PROB_NONE') }; $d | ForEach-Object InstanceId";

    internal static DiagnosticResult Diagnose()
    {
        try
        {
            var disabledAudioDevices = DisabledBluetoothAudioDeviceIds();
            var compatibilityState = CompatibilityModeStore.Load();
            var compatibilityModeActive = CompatibilityModeStore.Matches(compatibilityState, disabledAudioDevices);
            var registeredEndpointIds = RegisteredEndpointIds();
            var activeEndpointIds = registeredEndpointIds.Where(AudioEndpointNative.IsActive).ToArray();
            if (activeEndpointIds.Length > 0)
            {
                var volumePercent = CurrentVolumePercent(activeEndpointIds);
                return TraceDiagnosis(new(
                    compatibilityModeActive ? DeviceReadiness.ReadyCompatibility : DeviceReadiness.Ready,
                    compatibilityModeActive
                        ? "WU-BT10 AUDIO is ready in compatibility mode"
                        : "WU-BT10 AUDIO is ready",
                    volumePercent is null
                        ? ReadyMessage(compatibilityModeActive, null)
                        : ReadyMessage(compatibilityModeActive, volumePercent),
                    volumePercent),
                    disabledAudioDevices.Length,
                    null,
                    true,
                    compatibilityModeActive,
                    volumePercent);
            }

            var devices = BluetoothNative.FindWuBt10AudioDevices();
            if (compatibilityModeActive)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.CompatibilityDisconnected,
                    "Audio compatibility mode is active",
                    devices.Count == 0
                        ? "Pair WU-BT10 AUDIO in Windows. Intel Bluetooth-audio offload remains disabled only for this compatibility mode."
                        : "Open Windows Bluetooth settings and connect WU-BT10 AUDIO. This tested mode uses the standard Windows A2DP path and remains active across Audio/MIDI switches."),
                    disabledAudioDevices.Length,
                    devices.Count,
                    false,
                    true);
            }

            if (disabledAudioDevices.Length > 0)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.NeedsRepair,
                    "Windows Bluetooth Audio hardware is disabled",
                    "This device was not recorded as app-managed compatibility mode. The emergency action can only re-enable the already-disabled Intel Bluetooth Audio device after confirmation and UAC approval."),
                    disabledAudioDevices.Length,
                    devices.Count,
                    false,
                    false);
            }

            if (devices.Count == 0)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.NotPaired,
                    "Pair WU-BT10 AUDIO first",
                    "Open Windows Bluetooth settings, put the Casio instrument in Audio pairing mode, and pair WU-BT10 AUDIO."),
                    0,
                    0,
                    false,
                    false);
            }

            var bluetoothConnected = devices.Any(item => item.Connected);
            if (registeredEndpointIds.Length > 0 && !bluetoothConnected)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.ReadyDisconnected,
                    "WU-BT10 AUDIO is paired and currently disconnected",
                    "Open Windows Bluetooth settings and connect WU-BT10 AUDIO. If Windows connects without creating playback, enable the tested compatibility mode."),
                    0,
                    devices.Count,
                    false,
                    false);
            }

            var healthyIntelDevices = HealthyIntelBluetoothAudioDeviceIds();
            var healthyWuA2dpDevices = HealthyWuA2dpDeviceIds();
            if (bluetoothConnected && IsCompatibilityModeEligible(
                    healthyIntelDevices.Length,
                    healthyWuA2dpDevices.Length))
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.NeedsCompatibilityMode,
                    "Intel offload connected without a playback output",
                    "The tested compatibility mode disables only Intel Bluetooth-audio offload so Windows uses its standard A2DP path. It is reversible and requires one UAC approval."),
                    0,
                    devices.Count,
                    false,
                    false);
            }

            return TraceDiagnosis(new(
                DeviceReadiness.ManualAction,
                bluetoothConnected
                    ? "WU-BT10 is connected, but Windows has no playback output"
                    : "WU-BT10 AUDIO needs to be reconnected in Windows",
                "Open Bluetooth settings and reconnect the Audio device. The app will not reset Bluetooth profiles or restart Windows audio services."),
                0,
                devices.Count,
                false,
                false);
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("audio", "diagnosis-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            RepairLog.Write("Diagnosis failed: " + ex);
            return TraceDiagnosis(
                new(DeviceReadiness.Error, "Could not inspect Windows audio", "View the log for diagnostic details, then try again."),
                null,
                null,
                null,
                null);
        }
    }

    private static string ReadyMessage(bool compatibilityModeActive, int? volumePercent)
    {
        var volume = volumePercent is null
            ? "The playback endpoint is active."
            : $"The playback endpoint is active at {volumePercent}%.";
        var mode = compatibilityModeActive
            ? " Compatibility mode keeps Intel Bluetooth-audio offload disabled until you choose Restore Intel default mode."
            : string.Empty;
        return volume + " Diagnosis changed nothing." + mode + " Use the explicit 20% button only if wanted.";
    }

    private static DiagnosticResult TraceDiagnosis(
        DiagnosticResult result,
        int? disabledAudioDeviceCount,
        int? wuBt10AudioDeviceCount,
        bool? activeEndpointPresent,
        bool? compatibilityModeActive,
        int? volumePercent = null)
    {
        DiagnosticTrace.Write("audio", "diagnosis-completed", new Dictionary<string, object?>
        {
            ["state"] = result.State,
            ["title"] = result.Title,
            ["disabledAudioDeviceCount"] = disabledAudioDeviceCount,
            ["wuBt10AudioDeviceCount"] = wuBt10AudioDeviceCount,
            ["activeEndpointPresent"] = activeEndpointPresent,
            ["compatibilityModeActive"] = compatibilityModeActive,
            ["volumePercent"] = volumePercent
        });
        return result;
    }

    internal static RepairResult Run()
    {
        RepairLog.Begin();
        if (!IsAdministrator())
            return RepairResult.Failed("Administrator approval is required to restore a disabled Windows audio device.");

        var disabledAudioDevices = DisabledBluetoothAudioDeviceIds();
        if (CompatibilityModeStore.Matches(CompatibilityModeStore.Load(), disabledAudioDevices))
            return RepairResult.Failed("Audio compatibility mode is active. Use Restore Intel default mode instead of emergency repair.");
        if (disabledAudioDevices.Length == 0)
            return RepairResult.Failed("No disabled Intel Bluetooth Audio device was found. Nothing was changed.");
        if (disabledAudioDevices.Length != 1)
            return RepairResult.Failed("Expected exactly one disabled Intel Bluetooth Audio device. Nothing was changed.");

        return RestoreDisabledBluetoothAudioDevices(disabledAudioDevices);
    }

    internal static RepairResult EnableCompatibilityMode()
    {
        RepairLog.Begin();
        if (!IsAdministrator())
            return RepairResult.Failed("Administrator approval is required to enable Audio compatibility mode.");

        var disabledDevices = DisabledBluetoothAudioDeviceIds();
        var existingState = CompatibilityModeStore.Load();
        if (CompatibilityModeStore.Matches(existingState, disabledDevices))
            return RepairResult.Completed("Audio compatibility mode is already active.", EndpointReady());

        string instanceId;
        var changedDeviceState = false;
        if (disabledDevices.Length == 1)
        {
            if (HealthyWuA2dpDeviceIds().Length != 1)
                return RepairResult.Failed("Compatibility mode requires exactly one healthy WU-BT10 A2DP device. Nothing was changed.");

            instanceId = disabledDevices[0];
            RepairLog.Write("Adopting the existing disabled Intel Bluetooth Audio device as compatibility mode: " + instanceId);
        }
        else
        {
            var healthyIntelDevices = HealthyIntelBluetoothAudioDeviceIds();
            var healthyWuA2dpDevices = HealthyWuA2dpDeviceIds();
            if (!IsCompatibilityModeEligible(healthyIntelDevices.Length, healthyWuA2dpDevices.Length))
                return RepairResult.Failed("Compatibility mode requires exactly one healthy Intel Bluetooth Audio device and one healthy WU-BT10 A2DP device. Nothing was changed.");

            instanceId = healthyIntelDevices[0];
            try
            {
                RepairLog.Write("Enabling Audio compatibility mode for: " + instanceId);
                ProcessRunner.DisableDevice(instanceId);
                changedDeviceState = true;
                if (!WaitUntil(
                        () => DisabledBluetoothAudioDeviceIds().Contains(instanceId, StringComparer.OrdinalIgnoreCase),
                        TimeSpan.FromSeconds(5)))
                {
                    throw new InvalidOperationException("Windows did not confirm that Intel Bluetooth Audio was disabled.");
                }
            }
            catch (Exception ex)
            {
                RepairLog.Write("Could not enable compatibility mode: " + ex);
                if (changedDeviceState)
                    _ = RestoreIntelBluetoothAudio(instanceId);
                return RepairResult.Failed("Windows could not enable Audio compatibility mode. The app attempted to restore Intel default mode.");
            }
        }

        try
        {
            CompatibilityModeStore.Save(new(instanceId, DateTimeOffset.UtcNow));
            RepairLog.Write("Audio compatibility mode enabled and recorded.");
            return RepairResult.Completed(
                EndpointReady()
                    ? "Audio compatibility mode is active. WU-BT10 playback remains available."
                    : "Audio compatibility mode is active. In Windows Bluetooth settings, disconnect WU-BT10 AUDIO if necessary, then connect it again.",
                EndpointReady());
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not record compatibility mode: " + ex);
            if (changedDeviceState && RestoreIntelBluetoothAudio(instanceId))
                return RepairResult.Failed("Compatibility mode state could not be saved, so Intel default mode was restored.");
            return RepairResult.Failed("Compatibility mode state could not be saved. Check Intel Bluetooth Audio in Device Manager before continuing.");
        }
    }

    internal static RepairResult RestoreCompatibilityMode()
    {
        RepairLog.Begin();
        if (!IsAdministrator())
            return RepairResult.Failed("Administrator approval is required to restore Intel default mode.");

        var state = CompatibilityModeStore.Load();
        if (state is null)
            return RepairResult.Failed("The app has no recorded Audio compatibility mode to restore. Nothing was changed.");
        if (!state.IntelDeviceInstanceId.StartsWith("INTELAUDIO\\", StringComparison.OrdinalIgnoreCase))
            return RepairResult.Failed("The compatibility-mode record does not identify an Intel Audio device. Nothing was changed.");

        var knownIntelDevices = DisabledBluetoothAudioDeviceIds()
            .Concat(HealthyIntelBluetoothAudioDeviceIds())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!knownIntelDevices.Contains(state.IntelDeviceInstanceId, StringComparer.OrdinalIgnoreCase))
            return RepairResult.Failed("The recorded Intel Bluetooth Audio device is no longer present. Nothing was changed.");

        if (!RestoreIntelBluetoothAudio(state.IntelDeviceInstanceId))
            return RepairResult.Failed("Windows did not confirm restoration of Intel Bluetooth Audio. The compatibility-mode record was kept so recovery can be retried.");

        try
        {
            CompatibilityModeStore.Delete();
        }
        catch (Exception ex)
        {
            RepairLog.Write("Intel was restored but compatibility state could not be deleted: " + ex);
            return RepairResult.Failed("Intel default mode was restored, but the app could not clear its compatibility-mode record. See the log.");
        }

        return RepairResult.Completed(
            EndpointReady()
                ? "Intel default Bluetooth Audio mode was restored. WU-BT10 playback remains available until it disconnects."
                : "Intel default Bluetooth Audio mode was restored. Reconnect WU-BT10 AUDIO in Windows.",
            EndpointReady());
    }

    private static RepairResult RestoreDisabledBluetoothAudioDevices(IEnumerable<string> disabledAudioDevices)
    {
        var restored = true;
        foreach (var instanceId in disabledAudioDevices)
        {
            RepairLog.Write("Restoring disabled Bluetooth Audio device: " + instanceId);
            restored &= RestoreIntelBluetoothAudio(instanceId);
        }

        if (!restored)
            return RepairResult.Failed("Windows did not restore the disabled Bluetooth Audio device. No other system state was changed.");

        var endpointReady = EndpointReady();
        return RepairResult.Completed(
            endpointReady
                ? "Bluetooth Audio hardware was restored. WU-BT10 AUDIO is available; its volume was not changed."
                : "Bluetooth Audio hardware was restored. Reconnect WU-BT10 AUDIO in Windows Bluetooth settings.",
            endpointReady);
    }

    internal static bool IsCompatibilityModeEligible(
        int healthyIntelDeviceCount,
        int healthyWuA2dpDeviceCount) =>
        healthyIntelDeviceCount == 1 && healthyWuA2dpDeviceCount == 1;

    private static bool RestoreIntelBluetoothAudio(string instanceId)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            if (HealthyIntelBluetoothAudioDeviceIds().Contains(instanceId, StringComparer.OrdinalIgnoreCase))
                return true;

            try
            {
                ProcessRunner.EnableDevice(instanceId);
            }
            catch (Exception ex)
            {
                // pnputil can return 50 for an already-enabled device. Device state,
                // not the command exit code, is the authoritative restoration check.
                RepairLog.Write($"Intel restore command attempt {attempt}: {ex.Message}");
            }

            Thread.Sleep(1500);
            if (HealthyIntelBluetoothAudioDeviceIds().Contains(instanceId, StringComparer.OrdinalIgnoreCase))
                return true;

            try
            {
                ProcessRunner.ScanDevices();
            }
            catch (Exception ex)
            {
                RepairLog.Write("Device scan during Intel restoration failed: " + ex.Message);
            }
        }

        return HealthyIntelBluetoothAudioDeviceIds().Contains(instanceId, StringComparer.OrdinalIgnoreCase);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (condition())
                return true;
            Thread.Sleep(500);
        }
        while (DateTime.UtcNow < deadline);

        return condition();
    }

    private static string[] DisabledBluetoothAudioDeviceIds() =>
        ExpectedDeviceIds(ProcessRunner.PowerShell(DisabledBluetoothAudioQuery), "INTELAUDIO\\");

    private static string[] HealthyIntelBluetoothAudioDeviceIds() =>
        ExpectedDeviceIds(ProcessRunner.PowerShell(HealthyIntelBluetoothAudioQuery), "INTELAUDIO\\");

    private static string[] HealthyWuA2dpDeviceIds() =>
        ExpectedDeviceIds(
            ProcessRunner.PowerShell(HealthyWuA2dpQuery),
            "BTHENUM\\{0000110B-0000-1000-8000-00805F9B34FB}");

    private static string[] ExpectedDeviceIds(string output, string prefix) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool EndpointReady() => RegisteredEndpointIds().Any(AudioEndpointNative.IsActive);

    internal static bool IsAudioEndpointActive() => EndpointReady();

    internal static RepairResult SetSafeVolume(string? successMessage = null)
    {
        var activeIds = RegisteredEndpointIds().Where(AudioEndpointNative.IsActive).ToArray();
        if (activeIds.Length == 0)
            return RepairResult.Failed("WU-BT10 AUDIO is not currently available.");

        var changed = activeIds.Count(id => AudioEndpointNative.SetVolume(id, SafeVolume));
        return changed > 0
            ? RepairResult.Completed(successMessage ?? "Windows volume for WU-BT10 AUDIO is now 20%.")
            : RepairResult.Failed("Windows could not change the WU-BT10 volume.");
    }

    private static int? CurrentVolumePercent(IEnumerable<string> endpointIds)
    {
        foreach (var endpointId in endpointIds)
        {
            if (AudioEndpointNative.TryGetVolume(endpointId, out var volume))
                return checked((int)Math.Round(volume * 100, MidpointRounding.AwayFromZero));
        }
        return null;
    }

    private static string[] RegisteredEndpointIds() =>
        ProcessRunner.PowerShell(RegisteredEndpointQuery)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("SWD\\MMDEVAPI\\", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
