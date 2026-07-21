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
            if (disabledAudioDevices.Length > 0)
            {
                return new(
                    DeviceReadiness.NeedsRepair,
                    "Windows Bluetooth Audio hardware is disabled",
                    "Normal app functions remain available without elevation. The optional emergency action can only re-enable this already-disabled device after explicit confirmation and UAC approval.");
            }

            if (EndpointReady())
            {
                var volumeResult = SetSafeVolume();
                return new(
                    DeviceReadiness.Ready,
                    "WU-BT10 AUDIO is ready — Windows volume set to 20%",
                    volumeResult.Success
                        ? "Use the keyboard's volume control for listening loudness. The app changes only the active WU-BT10 endpoint."
                        : "The playback endpoint is active, but Windows did not accept the 20% volume setting.");
            }

            var devices = BluetoothNative.FindWuBt10AudioDevices();
            if (devices.Count == 0)
            {
                return new(
                    DeviceReadiness.NotPaired,
                    "Pair WU-BT10 AUDIO first",
                    "Open Windows Bluetooth settings, put the Casio instrument in Audio pairing mode, and pair WU-BT10 AUDIO.");
            }

            var bluetoothConnected = devices.Any(item => item.Connected);
            if (EndpointRegistered() && !bluetoothConnected)
            {
                return new(
                    DeviceReadiness.ReadyDisconnected,
                    "WU-BT10 AUDIO is paired and currently disconnected",
                    "Open Windows Bluetooth settings and connect WU-BT10 AUDIO. Windows should restore its playback output automatically.");
            }

            return new(
                DeviceReadiness.ManualAction,
                bluetoothConnected
                    ? "WU-BT10 is connected, but Windows has no playback output"
                    : "WU-BT10 AUDIO needs to be reconnected in Windows",
                "Open Bluetooth settings and reconnect the Audio device. The app will not reset Bluetooth profiles, disable system hardware, or restart audio services.");
        }
        catch (Exception ex)
        {
            RepairLog.Write("Diagnosis failed: " + ex);
            return new(DeviceReadiness.Error, "Could not inspect Windows audio", "View the log for diagnostic details, then try again.");
        }
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
                return SetSafeVolume("Bluetooth Audio hardware restored. WU-BT10 volume was set to 20%.");

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

    private static bool EndpointRegistered() => RegisteredEndpointIds().Length > 0;

    private static string[] RegisteredEndpointIds() =>
        ProcessRunner.PowerShell(RegisteredEndpointQuery)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("SWD\\MMDEVAPI\\", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

}
