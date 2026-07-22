using System.Security.Principal;

namespace WUBT10Fix;

internal static class RepairEngine
{
    internal const float SafeVolume = 0.20f;
    private const string RegisteredEndpointQuery = "Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | Where-Object FriendlyName -like '*WU-BT10 AUDIO*' | ForEach-Object InstanceId";
    private const string DisabledBluetoothAudioQuery = "$d=Get-PnpDevice -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'INTELAUDIO*' -and $_.FriendlyName -like '*Bluetooth*Audio*' -and ($_.Problem -eq 22 -or [string]$_.Problem -eq 'CM_PROB_DISABLED') }; $d | ForEach-Object InstanceId";

    internal static DiagnosticResult Diagnose()
    {
        try
        {
            var disabledAudioDevices = DisabledBluetoothAudioDeviceIds();
            var registeredEndpointIds = RegisteredEndpointIds();
            var activeEndpointIds = registeredEndpointIds.Where(AudioEndpointNative.IsActive).ToArray();
            if (activeEndpointIds.Length > 0)
            {
                var volumePercent = CurrentVolumePercent(activeEndpointIds);
                return TraceDiagnosis(new(
                    DeviceReadiness.Ready,
                    "WU-BT10 AUDIO is ready",
                    volumePercent is null
                        ? "The playback endpoint is active. Its volume was not changed; use the explicit 20% button if wanted."
                        : $"The playback endpoint is active at {volumePercent}%. Diagnosis did not change it; use the explicit 20% button if wanted.",
                    volumePercent),
                    0,
                    null,
                    true,
                    volumePercent);
            }

            if (disabledAudioDevices.Length > 0)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.NeedsRepair,
                    "Windows Bluetooth Audio hardware is disabled",
                    "Normal app functions remain available without elevation. The optional emergency action can only re-enable this already-disabled device after explicit confirmation and UAC approval."),
                    disabledAudioDevices.Length,
                    null,
                    false);
            }

            var devices = BluetoothNative.FindWuBt10AudioDevices();
            if (devices.Count == 0)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.NotPaired,
                    "Pair WU-BT10 AUDIO first",
                    "Open Windows Bluetooth settings, put the Casio instrument in Audio pairing mode, and pair WU-BT10 AUDIO."),
                    0,
                    0,
                    false);
            }

            var bluetoothConnected = devices.Any(item => item.Connected);
            if (registeredEndpointIds.Length > 0 && !bluetoothConnected)
            {
                return TraceDiagnosis(new(
                    DeviceReadiness.ReadyDisconnected,
                    "WU-BT10 AUDIO is paired and currently disconnected",
                    "Open Windows Bluetooth settings and connect WU-BT10 AUDIO. Windows should restore its playback output automatically."),
                    0,
                    devices.Count,
                    false);
            }

            return TraceDiagnosis(new(
                DeviceReadiness.ManualAction,
                bluetoothConnected
                    ? "WU-BT10 is connected, but Windows has no playback output"
                    : "WU-BT10 AUDIO needs to be reconnected in Windows",
                "Open Bluetooth settings and reconnect the Audio device. The app will not reset Bluetooth profiles, disable system hardware, or restart audio services."),
                0,
                devices.Count,
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
                null);
        }
    }

    private static DiagnosticResult TraceDiagnosis(
        DiagnosticResult result,
        int? disabledAudioDeviceCount,
        int? wuBt10AudioDeviceCount,
        bool? activeEndpointPresent,
        int? volumePercent = null)
    {
        DiagnosticTrace.Write("audio", "diagnosis-completed", new Dictionary<string, object?>
        {
            ["state"] = result.State,
            ["title"] = result.Title,
            ["disabledAudioDeviceCount"] = disabledAudioDeviceCount,
            ["wuBt10AudioDeviceCount"] = wuBt10AudioDeviceCount,
            ["activeEndpointPresent"] = activeEndpointPresent,
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
        if (disabledAudioDevices.Length == 0)
            return RepairResult.Failed("No disabled Intel Bluetooth Audio device was found. Nothing was changed.");

        try
        {
            foreach (var instanceId in disabledAudioDevices)
            {
                RepairLog.Write("Restoring disabled Bluetooth Audio device: " + instanceId);
                ProcessRunner.EnableDevice(instanceId);
            }

            Thread.Sleep(3000);
            var stillDisabled = DisabledBluetoothAudioDeviceIds();
            if (stillDisabled.Length > 0)
                return RepairResult.Failed("Windows did not restore the disabled Bluetooth Audio device. No other system state was changed.");

            if (EndpointReady())
            {
                return RepairResult.Completed(
                    "Bluetooth Audio hardware was restored. WU-BT10 AUDIO is available; its volume was not changed.");
            }

            return RepairResult.Completed(
                "Bluetooth Audio hardware was restored. Reconnect WU-BT10 AUDIO in Windows Bluetooth settings.",
                endpointPresent: false);
        }
        catch (Exception ex)
        {
            RepairLog.Write("Bluetooth Audio restoration failed: " + ex);
            return RepairResult.Failed("Windows could not enable the disabled Bluetooth Audio device. No disable or service-reset action was attempted.");
        }
        finally
        {
            RepairLog.Write("Safe enable-only repair finished.");
        }
    }

    private static string[] DisabledBluetoothAudioDeviceIds() =>
        ProcessRunner.PowerShell(DisabledBluetoothAudioQuery)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("INTELAUDIO\\", StringComparison.OrdinalIgnoreCase))
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
