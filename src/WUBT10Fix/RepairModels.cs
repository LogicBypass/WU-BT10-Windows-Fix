namespace WUBT10Fix;

internal enum DeviceReadiness
{
    Checking,
    Ready,
    ReadyCompatibility,
    ReadyDisconnected,
    NeedsRepair,
    NeedsCompatibilityMode,
    CompatibilityDisconnected,
    ManualAction,
    NotPaired,
    UnsupportedChipset,
    Error
}

internal sealed record DiagnosticResult(
    DeviceReadiness State,
    string Title,
    string Message,
    int? VolumePercent = null)
{
    internal static DiagnosticResult Checking() => new(DeviceReadiness.Checking, "Checking Windows audio", "Looking for WU-BT10 AUDIO and its playback endpoint…");
}

internal sealed record RepairResult(bool Success, string Message, bool EndpointPresent)
{
    internal static RepairResult Completed(string message) => new(true, message, true);
    internal static RepairResult Completed(string message, bool endpointPresent) => new(true, message, endpointPresent);
    internal static RepairResult Failed(string message) => new(false, message, false);
}
