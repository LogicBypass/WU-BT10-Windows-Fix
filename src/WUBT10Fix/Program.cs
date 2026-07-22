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

            if (args.Contains("--enable-audio-compatibility", StringComparer.OrdinalIgnoreCase))
                return RunElevatedAction(args, RepairEngine.EnableCompatibilityMode);

            if (args.Contains("--restore-intel-audio", StringComparer.OrdinalIgnoreCase))
                return RunElevatedAction(args, RepairEngine.RestoreCompatibilityMode);

            var diagnosticProbePath = ArgumentValue(args, "--diagnostic-probe-output");
            if (!string.IsNullOrWhiteSpace(diagnosticProbePath))
                return WriteDiagnosticProbe(diagnosticProbePath);

            var diagnosisPath = ArgumentValue(args, "--diagnose-output");
            if (!string.IsNullOrWhiteSpace(diagnosisPath))
                return WriteDiagnosis(diagnosisPath);

            var volumeResultPath = ArgumentValue(args, "--set-safe-volume");
            if (!string.IsNullOrWhiteSpace(volumeResultPath))
                return WriteSafeVolumeResult(volumeResultPath);

            var audioPlaybackTestPath = ArgumentValue(args, "--audio-playback-test-output");
            if (!string.IsNullOrWhiteSpace(audioPlaybackTestPath))
                return RunAudioPlaybackTestAsync(audioPlaybackTestPath).GetAwaiter().GetResult();

            var winMmProbePath = ArgumentValue(args, "--winmm-loopback-probe-output");
            if (!string.IsNullOrWhiteSpace(winMmProbePath))
                return RunWinMmLoopbackProbeAsync(winMmProbePath).GetAwaiter().GetResult();

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
                var holdDuration = TimeSpan.FromSeconds(
                    ArgumentInt(args, "--midi-cycle-hold-seconds", 0, 0, 60));
                var maintainConnection = ArgumentBool(args, "--midi-cycle-maintain-connection", false);
                return RunMidiCycleTestAsync(
                        midiCycleTestPath,
                        cycles,
                        trafficTimeout,
                        releaseTimeout,
                        holdDuration,
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
        mode is "gui" or "midi-test" or "midi-cycle-test" or "winmm-loopback-probe";

    private static int ReportInstanceConflict(string[] args, string mode)
    {
        const string message = "WU-BT10 Windows Fix is already running. Use the existing window or stop its MIDI bridge before starting another test.";
        DiagnosticTrace.Write("process", "single-instance-conflict", new Dictionary<string, object?>
        {
            ["mode"] = mode
        });

        var resultPath = ArgumentValue(args, mode switch
        {
            "midi-cycle-test" => "--midi-cycle-test-output",
            "winmm-loopback-probe" => "--winmm-loopback-probe-output",
            _ => "--midi-test-output"
        });
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
        if (args.Contains("--enable-audio-compatibility", StringComparer.OrdinalIgnoreCase)) return "enable-audio-compatibility";
        if (args.Contains("--restore-intel-audio", StringComparer.OrdinalIgnoreCase)) return "restore-intel-audio";
        if (ArgumentValue(args, "--diagnostic-probe-output") is not null) return "diagnostic-probe";
        if (ArgumentValue(args, "--diagnose-output") is not null) return "diagnose";
        if (ArgumentValue(args, "--set-safe-volume") is not null) return "set-safe-volume";
        if (ArgumentValue(args, "--audio-playback-test-output") is not null) return "audio-playback-test";
        if (ArgumentValue(args, "--winmm-loopback-probe-output") is not null) return "winmm-loopback-probe";
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
        using var mutationLease = SingleInstanceLease.TryAcquire(SingleInstanceLease.AudioMutationMutexName);
        if (!mutationLease.IsAcquired)
        {
            result = RepairResult.Failed("Another Audio compatibility or repair action is already running. Wait for it to finish, then diagnose again.");
        }
        else
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                RepairLog.Write(ex.ToString());
                result = RepairResult.Failed("The repair stopped unexpectedly. See the repair log for details.");
            }
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

    private static async Task<int> RunAudioPlaybackTestAsync(string path)
    {
        try
        {
            var result = await AudioPlaybackTester.PlayAsync();
            File.WriteAllText(path, JsonSerializer.Serialize(result));
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new AudioPlaybackTestResult(false, ex.Message)));
            return 1;
        }
    }

    private static async Task<int> RunWinMmLoopbackProbeAsync(string path)
    {
        var appToClient = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var clientToApp = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var appToClientReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientToAppReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var loopback = new WindowsMidiLoopback();
            using var appOutput = loopback.OpenOutput();
            using var clientInput = WinMmMidiInput.Open(WindowsMidiLoopback.PortName, message =>
            {
                appToClient.Enqueue(message);
                if (appToClient.Count >= 2)
                    appToClientReady.TrySetResult();
            });
            using var clientOutput = WinMmMidiOutput.Open(WindowsMidiLoopback.PortName);
            using var appInput = WindowsMidiLoopback.OpenInput(message =>
            {
                clientToApp.Enqueue(message);
                if (clientToApp.Count >= 2)
                    clientToAppReady.TrySetResult();
            });

            appOutput.Send([0x90, 0x3C, 0x40]);
            appOutput.Send([0xF0, 0x7D, 0x01, 0x02, 0xF7]);
            clientOutput.Send([0x80, 0x3C, 0x00]);
            clientOutput.Send([0xF0, 0x7D, 0x03, 0x04, 0xF7]);

            await Task.WhenAll(appToClientReady.Task, clientToAppReady.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Success = true,
                AppToClientMessages = appToClient.Count,
                ClientToAppMessages = clientToApp.Count,
                AppToClientByteCounts = appToClient.Select(message => message.Length).ToArray(),
                ClientToAppByteCounts = clientToApp.Select(message => message.Length).ToArray(),
                ElapsedMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
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
                Error = ex.ToString(),
                AppToClientMessages = appToClient.Count,
                ClientToAppMessages = clientToApp.Count,
                ElapsedMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                DiagnosticSession = DiagnosticTrace.SessionId,
                DiagnosticLog = DiagnosticTrace.CurrentPath
            }));
            return 1;
        }
    }

    private static async Task<int> RunMidiTestAsync(string path, TimeSpan listenDuration)
    {
        var messages = 0;
        var packets = 0;
        var sent = 0;
        string status = "Starting";
        try
        {
            await using var bridge = new MidiBridgeEngine();
            bridge.StatusChanged += value => status = value;
            bridge.MessageReceived += count => messages = count;
            bridge.PacketReceived += count => packets = count;
            bridge.MessageSent += count => sent = count;
            await bridge.StartAsync();
            await Task.Delay(listenDuration);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Success = true,
                Status = status,
                PacketsReceived = packets,
                MessagesReceived = messages,
                MessagesSent = sent,
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
                MessagesSent = sent,
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
        TimeSpan holdDuration,
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
            var sent = 0;
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
            void OnSent(int count) => sent = count;

            bridge.StatusChanged += OnStatus;
            bridge.PacketReceived += OnPacket;
            bridge.MessageReceived += OnMessage;
            bridge.MessageSent += OnSent;

            try
            {
                await bridge.StartAsync();
                if (trafficRequired)
                    await traffic.Task.WaitAsync(trafficTimeout);
                if (holdDuration > TimeSpan.Zero)
                    await Task.Delay(holdDuration);
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
                bridge.MessageSent -= OnSent;
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
                MessagesSent = sent,
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
            HoldSeconds = holdDuration.TotalSeconds,
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
