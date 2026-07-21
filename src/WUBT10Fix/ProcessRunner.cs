using System.Diagnostics;
using System.Text;

namespace WUBT10Fix;

internal static class ProcessRunner
{
    internal static string PowerShell(string command, TimeSpan? timeout = null)
    {
        var script = "$ProgressPreference='SilentlyContinue'; $WarningPreference='SilentlyContinue'; " + command;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return Run(
            System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            timeout ?? TimeSpan.FromSeconds(60),
            "PowerShell device query");
    }

    internal static void EnableDevice(string instanceId) =>
        Run(
            System.IO.Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            ["/enable-device", instanceId],
            TimeSpan.FromSeconds(60));

    internal static string Run(string fileName, IEnumerable<string> arguments, TimeSpan timeout, string? logDescription = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + fileName);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(System.IO.Path.GetFileName(fileName) + " timed out.");
        }

        Task.WaitAll(outputTask, errorTask);
        var output = outputTask.Result;
        var error = errorTask.Result;
        var commandDescription = logDescription
            ?? $"{System.IO.Path.GetFileName(fileName)} {string.Join(' ', info.ArgumentList)}";
        RepairLog.Write($"> {commandDescription}{Environment.NewLine}{output}{Environment.NewLine}{error}");

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(fileName)} failed ({process.ExitCode}): {error}{output}".Trim());

        return output;
    }
}
