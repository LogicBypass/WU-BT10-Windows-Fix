using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace WUBT10Fix;

internal static class RepairLauncher
{
    internal static async Task<RepairResult> RunElevatedAsync()
    {
        var resultPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"wubt10-fix-result-{Guid.NewGuid():N}.json");

        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable path is unavailable.");

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--repair");
            startInfo.ArgumentList.Add("--result");
            startInfo.ArgumentList.Add(resultPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start the repair helper.");
            await process.WaitForExitAsync();

            if (!File.Exists(resultPath))
                return RepairResult.Failed("The elevated repair did not return a result. See the repair log for details.");

            var json = await File.ReadAllTextAsync(resultPath);
            return JsonSerializer.Deserialize<RepairResult>(json)
                ?? RepairResult.Failed("Windows returned an unreadable repair result.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return RepairResult.Failed("Administrator approval was canceled. No changes were made.");
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not launch elevated repair: " + ex);
            return RepairResult.Failed("Windows could not start the elevated repair. See the log for details.");
        }
        finally
        {
            try
            {
                if (File.Exists(resultPath))
                    File.Delete(resultPath);
            }
            catch
            {
                // A stale result file contains no credentials or private user data.
            }
        }
    }
}
