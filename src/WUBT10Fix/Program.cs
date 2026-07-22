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

            var audioConnectPath = ArgumentValue(args, "--audio-connect-output");
            if (!string.IsNullOrWhiteSpace(audioConnectPath))
                return RunAudioSwitchAsync(audioConnectPath, true).GetAwaiter().GetResult();

            var audioDisconnectPath = ArgumentValue(args, "--audio-disconnect-output");
            if (!string.IsNullOrWhiteSpace(audioDisconnectPath))
                return RunAudioSwitchAsync(audioDisconnectPath, false).GetAwaiter().GetResult();

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
                var sendSelfTestNote = ArgumentBool(args, "--midi-cycle-send-note", false);
                return RunMidiCycleTestAsync(
                        midiCycleTestPath,
                        cycles,
                        trafficTimeout,
                        releaseTimeout,
                        holdDuration,
                        maintainConnection,
                        sendSelfTestNote)
                    .GetAwaiter()
                    .GetResult();
            }

            var integratedCyclePath = ArgumentValue(args, "--integrated-cycle-test-output");
            if (!string.IsNullOrWhiteSpace(integratedCyclePath))
            {
                var cycles = ArgumentInt(args, "--integrated-cycle-count", 1, 1, 25);
                return RunIntegratedCycleTestAsync(integratedCyclePath, cycles)
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
        mode is "gui" or "midi-test" or "midi-cycle-test" or "integrated-cycle-test" or "winmm-loopback-probe";

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
            "integrated-cycle-test" => "--integrated-cycle-test-output",
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
        if (ArgumentValue(args, "--audio-connect-output") is not null) return "audio-connect";
        if (ArgumentValue(args, "--audio-disconnect-output") is not null) return "audio-disconnect";
        if (ArgumentValue(args, "--winmm-loopback-probe-output") is not null) return "winmm-loopback-probe";
        if (ArgumentValue(args, "--midi-test-output") is not null) return "midi-test";
        if (ArgumentValue(args, "--midi-cycle-test-output") is not null) return "midi-cycle-test";
        if (ArgumentValue(args, "--integrated-cycle-test-output") is not null) return "integrated-cycle-test";
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
        using var mutationLease = AudioOperationLease.TryAcquire();
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

    private static async Task<int> RunAudioSwitchAsync(string path, bool connect)
    {
        AudioSwitchResult result;
        try
        {
            result = connect
                ? await AudioConnectionController.ConnectAsync()
                : await AudioConnectionController.DisconnectAsync();
        }
        catch (Exception ex)
        {
            result = AudioSwitchResult.Failed(ex.Message);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
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
            await using var bridge = new MidiBridgeEngine(maintainConnection: true);
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
        bool maintainConnection,
        bool sendSelfTestNote)
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
            var requiredSentMessages = sendSelfTestNote ? 3 : 0;
            var traffic = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var outboundComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var advertisementRestored = false;
            string? error = null;

            void OnStatus(string value) => status = value;
            void OnPacket(int count)
            {
                packets = count;
                traffic.TrySetResult();
            }
            void OnMessage(int count) => messages = count;
            void OnSent(int count)
            {
                sent = count;
                if (count >= requiredSentMessages)
                    outboundComplete.TrySetResult();
            }

            bridge.StatusChanged += OnStatus;
            bridge.PacketReceived += OnPacket;
            bridge.MessageReceived += OnMessage;
            bridge.MessageSent += OnSent;

            try
            {
                await bridge.StartAsync();
                if (sendSelfTestNote)
                {
                    using var testOutput = WinMmMidiOutput.Open(WindowsMidiLoopback.PortName);
                    testOutput.Send([0x90, 0x3C, 0x08]);
                    await Task.Delay(TimeSpan.FromMilliseconds(120));
                    testOutput.Send([0x80, 0x3C, 0x00]);
                    testOutput.Send([0xB0, 0x7B, 0x00]);
                    await outboundComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
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
                Success = error is null
                    && (!trafficRequired || packets > 0)
                    && (!sendSelfTestNote || sent >= requiredSentMessages)
                    && advertisementRestored,
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
            SendSelfTestNote = sendSelfTestNote,
            Results = results,
            DiagnosticSession = DiagnosticTrace.SessionId,
            DiagnosticLog = DiagnosticTrace.CurrentPath
        }));

        return allSucceeded && results.Count == requestedCycles ? 0 : 1;
    }

    private static async Task<int> RunIntegratedCycleTestAsync(string path, int requestedCycles)
    {
        var results = new List<object>();
        var allSucceeded = true;

        for (var cycle = 1; cycle <= requestedCycles; cycle++)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var phase = "prepare";
            var sent = 0;
            var checkpoints = new List<object>();
            string? error = null;
            MidiBridgeEngine? bridge = null;

            try
            {
                phase = "ensure-audio-mode";
                var audioMode = await AudioConnectionController.ConnectAsync();
                EnsureAudioSwitchSucceeded(audioMode, phase);
                EnsureSafeAudioConnected(phase);
                checkpoints.Add(CaptureIntegratedCheckpoint(phase, sent));

                phase = "coordinated-audio-to-midi-handoff";
                bridge = new MidiBridgeEngine(
                    maintainConnection: true,
                    afterMaintainConnection: async cancellationToken =>
                    {
                        var release = await AudioConnectionController.DisconnectAsync(cancellationToken);
                        EnsureAudioSwitchSucceeded(release, "release-audio-after-gatt-maintain-request");
                    });
                bridge.MessageSent += count => sent = count;
                await bridge.StartAsync();
                using var testOutput = WinMmMidiOutput.Open(WindowsMidiLoopback.PortName);
                await SendMidiSelfTestNoteAsync(testOutput, () => sent, expectedSentMessages: 3);
                if (RepairEngine.IsAudioEndpointActive()
                    || BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected))
                {
                    throw new InvalidOperationException("WU-BT10 Audio remained connected after the coordinated MIDI handoff.");
                }
                checkpoints.Add(CaptureIntegratedCheckpoint(phase, sent));

                phase = "midi-to-audio-handoff";
                await bridge.StopAsync();
                await WaitForMidiAdvertisementAsync(TimeSpan.FromSeconds(15));
                var audioReconnect = await AudioConnectionController.ConnectAsync();
                EnsureAudioSwitchSucceeded(audioReconnect, phase);
                EnsureSafeAudioConnected(phase);
                checkpoints.Add(CaptureIntegratedCheckpoint(phase, sent));
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                allSucceeded = false;
            }
            finally
            {
                if (bridge is not null)
                {
                    try
                    {
                        await bridge.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        error = error is null ? ex.ToString() : error + Environment.NewLine + ex;
                        allSucceeded = false;
                    }
                }

            }

            results.Add(new
            {
                Cycle = cycle,
                Success = error is null,
                LastPhase = phase,
                MessagesSent = sent,
                ElapsedMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                Checkpoints = checkpoints,
                Error = error
            });

            if (error is not null)
                break;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Success = allSucceeded && results.Count == requestedCycles,
            RequestedCycles = requestedCycles,
            CompletedCycles = results.Count,
            Results = results,
            DiagnosticSession = DiagnosticTrace.SessionId,
            DiagnosticLog = DiagnosticTrace.CurrentPath
        }));

        return allSucceeded && results.Count == requestedCycles ? 0 : 1;
    }

    private static async Task SendMidiSelfTestNoteAsync(
        WinMmMidiOutput output,
        Func<int> sentCount,
        int expectedSentMessages)
    {
        output.Send([0x90, 0x3C, 0x08]);
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        output.Send([0x80, 0x3C, 0x00]);
        output.Send([0xB0, 0x7B, 0x00]);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (sentCount() >= expectedSentMessages)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException(
            $"Only {sentCount()} of {expectedSentMessages} expected BLE-MIDI safety messages were sent within 5 seconds.");
    }

    private static async Task WaitForMidiAdvertisementAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        _ = await MidiBridgeEngine.FindAdvertisingWuBt10Async(timeout, cancellation.Token);
    }

    private static void EnsureAudioSwitchSucceeded(AudioSwitchResult result, string phase)
    {
        if (!result.Success)
            throw new InvalidOperationException($"{phase}: {result.Message}");
    }

    private static void EnsureSafeAudioConnected(string phase)
    {
        var activeIds = RepairEngine.ActiveAudioEndpointIds();
        var transportConnected = BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected);
        var safeVolume = activeIds.Length == 1
            && AudioEndpointNative.TryGetVolume(activeIds[0], out var volume)
            && volume <= RepairEngine.SafeVolume + 0.005f;
        if (activeIds.Length != 1 || !transportConnected || !safeVolume)
        {
            throw new InvalidOperationException(
                $"{phase}: Audio verification failed (active endpoints: {activeIds.Length}, transport connected: {transportConnected}, safe volume: {safeVolume}).");
        }
    }

    private static object CaptureIntegratedCheckpoint(string phase, int messagesSent)
    {
        var activeIds = RepairEngine.ActiveAudioEndpointIds();
        float? volumePercent = null;
        if (activeIds.Length == 1 && AudioEndpointNative.TryGetVolume(activeIds[0], out var volume))
            volumePercent = volume * 100;

        return new
        {
            Phase = phase,
            AudioEndpointActive = activeIds.Length == 1,
            AudioTransportConnected = BluetoothNative.FindWuBt10AudioDevices().Any(device => device.Connected),
            VolumePercent = volumePercent,
            MidiMessagesSent = messagesSent
        };
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
