namespace WUBT10Fix;

internal static class RepairLog
{
    internal static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WU-BT10-Fix",
        "repair.log");

    internal static void Begin()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.AppendAllText(Path, $"{Environment.NewLine}=== Repair {DateTimeOffset.Now:O} ==={Environment.NewLine}");
    }

    internal static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never prevent device restoration or application exit.
        }
    }
}
