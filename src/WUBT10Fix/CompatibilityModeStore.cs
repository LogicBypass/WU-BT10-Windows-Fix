using System.Text.Json;

namespace WUBT10Fix;

internal sealed record CompatibilityModeState(string IntelDeviceInstanceId, DateTimeOffset EnabledAtUtc);

internal static class CompatibilityModeStore
{
    internal static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WU-BT10-Fix",
        "audio-compatibility-mode.json");

    internal static CompatibilityModeState? Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<CompatibilityModeState>(File.ReadAllText(Path))
                : null;
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not read Audio compatibility-mode state: " + ex.Message);
            return null;
        }
    }

    internal static void Save(CompatibilityModeState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporaryPath = Path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // The state contains only a PnP instance ID and timestamp.
            }
        }
    }

    internal static void Delete()
    {
        if (File.Exists(Path))
            File.Delete(Path);
    }

    internal static bool Matches(CompatibilityModeState? state, IEnumerable<string> disabledDeviceIds) =>
        state is not null
        && disabledDeviceIds.Contains(state.IntelDeviceInstanceId, StringComparer.OrdinalIgnoreCase);
}
