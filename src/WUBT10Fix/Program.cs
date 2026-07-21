using System.Text.Json;

namespace WUBT10Fix;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        DiagnosticTrace.Begin(DetermineMode(args));
        try
        {
            if (args.Contains("--repair", StringComparer.OrdinalIgnoreCase))
                return RunElevatedAction(args, RepairEngine.Run);

            var diagnosticProbePath = ArgumentValue(args, "--diagnostic-probe-output");
            if (!string.IsNullOrWhiteSpace(diagnosticProbePath))
                return WriteDiagnosticProbe(diagnosticProbePath);

            var diagnosisPath = ArgumentValue(args, "--diagnose-output");
            if (!string.IsNullOrWhiteSpace(diagnosisPath))
                return WriteDiagnosis(diagnosisPath);

            var volumeResultPath = ArgumentValue(args, "--set-safe-volume");
            if (!string.IsNullOrWhiteSpace(volumeResultPath))
                return WriteSafeVolumeResult(volumeResultPath);

            var midiTestPath = ArgumentValue(args, "--midi-test-output");
            if (!string.IsNullOrWhiteSpace(midiTestPath))
            {
                var durationText = ArgumentValue(args, "--midi-test-seconds");
                var duration = int.TryParse(durationText, out var seconds)
                    ? TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 120))
                    : TimeSpan.FromSeconds(3);
                return RunMidiTestAsync(midiTestPath, duration).GetAwaiter().GetResult();
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
        finally
        {
            DiagnosticTrace.End("main-returned");
        }
    }

    private static string DetermineMode(string[] args)
    {
        if (args.Contains("--repair", StringComparer.OrdinalIgnoreCase)) return "repair";
        if (ArgumentValue(args, "--diagnostic-probe-output") is not null) return "diagnostic-probe";
        if (ArgumentValue(args, "--diagnose-output") is not null) return "diagnose";
        if (ArgumentValue(args, "--set-safe-volume") is not null) return "set-safe-volume";
        if (ArgumentValue(args, "--midi-test-output") is not null) return "midi-test";
        return "gui";
    }

    private static int WriteDiagnosticProbe(string path)
    {
        try
        {
            DiagnosticTrace.Write("process", "diagnostic-probe");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Success = true,
                Version = Application.ProductVersion,
                DiagnosticSession = DiagnosticTrace.SessionId,
                DiagnosticLog = DiagnosticTrace.CurrentPath
            }));
            return 0;
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not write diagnostic probe: " + ex);
            return 1;
        }
    }

    private static int RunElevatedAction(string[] args, Func<RepairResult> action)
    {
        var resultPath = ArgumentValue(args, "--result");
        RepairResult result;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            RepairLog.Write(ex.ToString());
            result = RepairResult.Failed("The repair stopped unexpectedly. See the repair log for details.");
        }

        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            try
            {
                File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                RepairLog.Write("Could not write repair result: " + ex);
            }
        }

        return result.Success ? 0 : 1;
    }

    private static int WriteDiagnosis(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(RepairEngine.Diagnose()));
            return 0;
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not write diagnosis: " + ex);
            return 1;
        }
    }

    private static int WriteSafeVolumeResult(string path)
    {
        try
        {
            var result = RepairEngine.SetSafeVolume();
            File.WriteAllText(path, JsonSerializer.Serialize(result));
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not set safe volume: " + ex);
            return 1;
        }
    }

    private static async Task<int> RunMidiTestAsync(string path, TimeSpan listenDuration)
    {
        var messages = 0;
        var packets = 0;
        string status = "Starting";
        try
        {
            await using var bridge = new MidiBridgeEngine();
            bridge.StatusChanged += value => status = value;
            bridge.MessageReceived += count => messages = count;
            bridge.PacketReceived += count => packets = count;
            await bridge.StartAsync();
            await Task.Delay(listenDuration);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Success = true,
                Status = status,
                PacketsReceived = packets,
                MessagesReceived = messages,
                Port = WindowsMidiLoopback.PortName,
                DiagnosticSession = DiagnosticTrace.SessionId,
                DiagnosticLog = DiagnosticTrace.CurrentPath
            }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Success = false,
                Status = status,
                Error = ex.Message,
                PacketsReceived = packets,
                MessagesReceived = messages,
                DiagnosticSession = DiagnosticTrace.SessionId,
                DiagnosticLog = DiagnosticTrace.CurrentPath
            }));
            return 1;
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
