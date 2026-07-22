using System.Text.Json;

namespace WUBT10Fix;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var mode = DetermineMode(args);
        DiagnosticTrace.Begin(mode);
        try
        {
            using var instanceLease = RequiresExclusiveInstance(mode)
                ? SingleInstanceLease.TryAcquire()
                : null;
            if (instanceLease is { IsAcquired: false })
                return ReportInstanceConflict(args, mode);

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

            var midiCycleTestPath = ArgumentValue(args, "--midi-cycle-test-output");
            if (!string.IsNullOrWhiteSpace(midiCycleTestPath))
            {
                var cycles = ArgumentInt(args, "--midi-cycle-count", 5, 1, 100);
                var trafficTimeout = TimeSpan.FromSeconds(
                    ArgumentInt(args, "--midi-cycle-traffic-seconds", 0, 0, 60));
                var releaseTimeout = TimeSpan.FromSeconds(
                    ArgumentInt(args, "--midi-cycle-release-seconds", 10, 1, 60));
                var maintainConnection = ArgumentBool(args, "--midi-cycle-maintain-connection", false);
                return RunMidiCycleTestAsync(
                        midiCycleTestPath,
                        cycles,
                        trafficTimeout,
                        releaseTimeout,
                        maintainConnection)
                    .GetAwaiter()
                    .GetResult();
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

    private static bool RequiresExclusiveInstance(string mode) =>
        mode is "gui" or "midi-test" or "midi-cycle-test";

    private static int ReportInstanceConflict(string[] args, string mode)
    {
        const string message = "WU-BT10 Windows Fix is already running. Use the existing window or stop its MIDI bridge before starting another test.";
        DiagnosticTrace.Write("process", "single-instance-conflict", new Dictionary<string, object?>
        {
            ["mode"] = mode
        });

        var resultPath = ArgumentValue(args, mode == "midi-cycle-test"
            ? "--midi-cycle-test-output"
            : "--midi-test-output");
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = false,
                Error = message,
                DiagnosticSession = DiagnosticTrace.SessionId,
                DiagnosticLog = DiagnosticTrace.CurrentPath
            }));
        }
        else
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show(message, "WU-BT10 Windows Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return 2;
    }

    private static string DetermineMode(string[] args)
    {
        if (args.Contains("--repair", StringComparer.OrdinalIgnoreCase)) return "repair";
        if (ArgumentValue(args, "--diagnostic-probe-output") is not null) return "diagnostic-probe";
        if (ArgumentValue(args, "--diagnose-output") is not null) return "diagnose";
        if (ArgumentValue(args, "--set-safe-volume") is not null) return "set-safe-volume";
        if (ArgumentValue(args, "--midi-test-output") is not null) return "midi-test";
        if (ArgumentValue(args, "--midi-cycle-test-output") is not null) return "midi-cycle-test";
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

    private static async Task<int> RunMidiCycleTestAsync(
        string path,
        int requestedCycles,
        TimeSpan trafficTimeout,
        TimeSpan releaseTimeout,
        bool maintainConnection)
    {
        var results = new List<object>();
        var allSucceeded = true;

        await using var bridge = new MidiBridgeEngine(maintainConnection);
        for (var cycle = 1; cycle <= requestedCycles; cycle++)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var packets = 0;
            var messages = 0;
            var status = "Starting";
            var trafficRequired = trafficTimeout > TimeSpan.Zero;
            var traffic = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var advertisementRestored = false;
            string? error = null;

            void OnStatus(string value) => status = value;
            void OnPacket(int count)
            {
                packets = count;
                traffic.TrySetResult();
            }
            void OnMessage(int count) => messages = count;

            bridge.StatusChanged += OnStatus;
            bridge.PacketReceived += OnPacket;
            bridge.MessageReceived += OnMessage;

            try
            {
                await bridge.StartAsync();
                if (trafficRequired)
                    await traffic.Task.WaitAsync(trafficTimeout);
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                allSucceeded = false;
            }
            finally
            {
                try
                {
                    await bridge.StopAsync();
                }
                catch (Exception ex)
                {
                    error = error is null ? ex.ToString() : error + Environment.NewLine + ex;
                    allSucceeded = false;
                }

                bridge.StatusChanged -= OnStatus;
                bridge.PacketReceived -= OnPacket;
                bridge.MessageReceived -= OnMessage;
            }

            using (var releaseCancellation = new CancellationTokenSource(releaseTimeout))
            {
                try
                {
                    _ = await MidiBridgeEngine.FindAdvertisingWuBt10Async(
                        releaseTimeout,
                        releaseCancellation.Token);
                    advertisementRestored = true;
                }
                catch (OperationCanceledException)
                {
                    var releaseError = $"WU-BT10 did not advertise within {releaseTimeout.TotalSeconds:N0} seconds after Stop.";
                    error = error is null ? releaseError : error + Environment.NewLine + releaseError;
                    allSucceeded = false;
                }
                catch (Exception ex)
                {
                    error = error is null ? ex.ToString() : error + Environment.NewLine + ex;
                    allSucceeded = false;
                }
            }

            results.Add(new
            {
                Cycle = cycle,
                Success = error is null && (!trafficRequired || packets > 0) && advertisementRestored,
                StartedAt = startedAt,
                ElapsedMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                Status = status,
                PacketsReceived = packets,
                MessagesReceived = messages,
                AdvertisementRestored = advertisementRestored,
                Error = error
            });

            if (!allSucceeded)
                break;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Success = allSucceeded && results.Count == requestedCycles,
            RequestedCycles = requestedCycles,
            CompletedCycles = results.Count,
            TrafficTimeoutSeconds = trafficTimeout.TotalSeconds,
            TrafficRequired = trafficTimeout > TimeSpan.Zero,
            ReleaseTimeoutSeconds = releaseTimeout.TotalSeconds,
            MaintainConnection = maintainConnection,
            Results = results,
            DiagnosticSession = DiagnosticTrace.SessionId,
            DiagnosticLog = DiagnosticTrace.CurrentPath
        }));

        return allSucceeded && results.Count == requestedCycles ? 0 : 1;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ArgumentInt(string[] args, string name, int defaultValue, int minimum, int maximum)
    {
        var value = ArgumentValue(args, name);
        return int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : defaultValue;
    }

    private static bool ArgumentBool(string[] args, string name, bool defaultValue)
    {
        var value = ArgumentValue(args, name);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
