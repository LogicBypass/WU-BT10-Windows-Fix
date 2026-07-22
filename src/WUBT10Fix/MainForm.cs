using System.Diagnostics;
using System.Reflection;

namespace WUBT10Fix;

internal sealed class MainForm : Form
{
    private const string WindowsMidiServicesUrl = "https://microsoft.github.io/MIDI/get-latest/";
    private static readonly Color Navy = Color.FromArgb(11, 31, 63);
    private static readonly Color Cyan = Color.FromArgb(0, 174, 214);
    private static readonly Color Green = Color.FromArgb(22, 137, 82);
    private static readonly Color Amber = Color.FromArgb(184, 115, 0);
    private static readonly Color SoftBackground = Color.FromArgb(246, 248, 251);

    private readonly Label _statusTitle = new();
    private readonly Label _statusMessage = new();
    private readonly Label _statusMark = new();
    private readonly Button _repairButton = new();
    private readonly Button _audioConnectButton = new();
    private readonly Button _supportButton = new();
    private readonly Button _logButton = new();
    private readonly ProgressBar _progress = new();
    private readonly MidiBridgeEngine _midiBridge;
    private readonly Label _midiStatusTitle = new();
    private readonly Label _midiStatusMessage = new();
    private readonly Label _midiStatusMark = new();
    private readonly Button _midiButton = new();
    private bool _closingAfterMidiStop;
    private bool _refreshInProgress;
    private int _midiReceivedCount;
    private int _midiSentCount;
    private DateTime _lastAudioRefresh = DateTime.MinValue;

    internal MainForm()
    {
        _midiBridge = new MidiBridgeEngine(
            maintainConnection: true,
            afterMaintainConnection: ReleaseAudioForMidiHandoffAsync);
        Text = "WU-BT10 Windows Fix";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 540);
        ClientSize = new Size(760, 600);
        BackColor = SoftBackground;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        _midiBridge.StatusChanged += OnMidiStatusChanged;
        _midiBridge.MessageReceived += OnMidiMessageReceived;
        _midiBridge.MessageSent += OnMidiMessageSent;
        _midiBridge.StateChanged += OnMidiStateChanged;
        Shown += async (_, _) => await RefreshDiagnosisAsync();
        Activated += async (_, _) =>
        {
            if (DateTime.UtcNow - _lastAudioRefresh > TimeSpan.FromSeconds(2))
                await RefreshDiagnosisAsync();
        };
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 108,
            BackColor = Navy,
            Padding = new Padding(28, 16, 28, 14)
        };

        var logo = new PictureBox
        {
            Location = new Point(28, 14),
            Size = new Size(78, 78),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadLogo(),
            AccessibleName = "Wireless piano connection icon"
        };

        var title = new Label
        {
            Text = "WU-BT10 Audio / MIDI",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20F),
            AutoSize = true,
            Location = new Point(124, 22)
        };

        var subtitle = new Label
        {
            Text = "One safe control for your Casio PX-S1100",
            ForeColor = Color.FromArgb(203, 220, 239),
            Font = new Font("Segoe UI", 10.5F),
            AutoSize = true,
            Location = new Point(128, 64)
        };

        header.Controls.AddRange([logo, title, subtitle]);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 30, 32, 22),
            AutoScroll = true
        };

        var statusCard = new Panel
        {
            Location = new Point(32, 30),
            Size = new Size(676, 142),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _statusMark.Text = "●";
        _statusMark.Font = new Font("Segoe UI", 24F);
        _statusMark.ForeColor = Cyan;
        _statusMark.AutoSize = true;
        _statusMark.Location = new Point(22, 26);

        _statusTitle.Text = "Checking WU-BT10";
        _statusTitle.Font = new Font("Segoe UI Semibold", 13F);
        _statusTitle.ForeColor = Navy;
        _statusTitle.AutoSize = false;
        _statusTitle.Location = new Point(72, 22);
        _statusTitle.Size = new Size(565, 30);

        _statusMessage.Text = "Checking Audio and MIDI availability…";
        _statusMessage.ForeColor = Color.FromArgb(70, 79, 92);
        _statusMessage.AutoSize = false;
        _statusMessage.Location = new Point(75, 56);
        _statusMessage.Size = new Size(555, 68);

        statusCard.Controls.AddRange([_statusMark, _statusTitle, _statusMessage]);

        _progress.Location = new Point(32, 184);
        _progress.Size = new Size(676, 7);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 25;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        _audioConnectButton.Text = "Connect Audio";
        _audioConnectButton.Font = new Font("Segoe UI Semibold", 14F);
        _audioConnectButton.BackColor = Cyan;
        _audioConnectButton.ForeColor = Color.White;
        _audioConnectButton.FlatStyle = FlatStyle.Flat;
        _audioConnectButton.FlatAppearance.BorderSize = 0;
        _audioConnectButton.Location = new Point(32, 218);
        _audioConnectButton.Size = new Size(676, 60);
        _audioConnectButton.Click += async (_, _) => await ToggleModeAsync();

        _repairButton.Text = "Emergency: re-enable device";
        _repairButton.Font = new Font("Segoe UI", 9F);
        _repairButton.BackColor = Color.Transparent;
        _repairButton.ForeColor = Color.FromArgb(92, 101, 114);
        _repairButton.FlatStyle = FlatStyle.Flat;
        _repairButton.FlatAppearance.BorderColor = Color.FromArgb(208, 213, 220);
        _repairButton.Location = new Point(448, 384);
        _repairButton.Size = new Size(260, 32);
        _repairButton.Visible = false;
        _repairButton.Click += async (_, _) => await RepairAsync();

        _supportButton.Visible = false;

        _logButton.Text = "Diagnostics";
        _logButton.FlatStyle = FlatStyle.Flat;
        _logButton.ForeColor = Navy;
        _logButton.Location = new Point(32, 384);
        _logButton.Size = new Size(112, 32);
        _logButton.Click += (_, _) => OpenLog();

        var modeExplanation = new Label
        {
            Text = "The button switches the complete connection safely",
            Font = new Font("Segoe UI Semibold", 11.5F),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(32, 310)
        };

        var modeDetails = new Label
        {
            Text = "Audio is always limited to 20%. When switching to MIDI, the app prepares the BLE session before releasing Audio so the piano stays available. No Windows Settings, test tone, pairing change, or administrator approval is used during normal switching.",
            ForeColor = Color.FromArgb(70, 79, 92),
            AutoSize = false,
            Location = new Point(32, 340),
            Size = new Size(676, 58),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var privacyText = new Label
        {
            Text = $"Open source • No network access during repair • Version {Application.ProductVersion.Split('+')[0]}",
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(32, 438)
        };

        content.Controls.AddRange([
            statusCard,
            _progress,
            _audioConnectButton,
            _repairButton,
            _supportButton,
            _logButton,
            modeExplanation,
            modeDetails,
            privacyText
        ]);

        Controls.Add(content);
        Controls.Add(header);
    }

    private TabPage BuildMidiPage()
    {
        var page = new TabPage("MIDI")
        {
            BackColor = SoftBackground,
            Font = new Font("Segoe UI", 10F)
        };
        var card = new Panel
        {
            Location = new Point(32, 28),
            Size = new Size(696, 126),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _midiStatusMark.Text = "●";
        _midiStatusMark.Font = new Font("Segoe UI", 24F);
        _midiStatusMark.ForeColor = Amber;
        _midiStatusMark.AutoSize = true;
        _midiStatusMark.Location = new Point(22, 26);

        _midiStatusTitle.Text = "Bluetooth MIDI bridge is stopped";
        _midiStatusTitle.Font = new Font("Segoe UI Semibold", 13F);
        _midiStatusTitle.ForeColor = Navy;
        _midiStatusTitle.AutoSize = false;
        _midiStatusTitle.Location = new Point(72, 22);
        _midiStatusTitle.Size = new Size(590, 30);

        _midiStatusMessage.Text = "Start the bridge, then select WU-BT10 MIDI Bridge in Piano Marvel or your MIDI application.";
        _midiStatusMessage.ForeColor = Color.FromArgb(70, 79, 92);
        _midiStatusMessage.AutoSize = false;
        _midiStatusMessage.Location = new Point(75, 56);
        _midiStatusMessage.Size = new Size(585, 55);
        card.Controls.AddRange([_midiStatusMark, _midiStatusTitle, _midiStatusMessage]);

        _midiButton.Text = "Connect WU-BT10 MIDI";
        _midiButton.Font = new Font("Segoe UI Semibold", 10.5F);
        _midiButton.BackColor = Cyan;
        _midiButton.ForeColor = Color.White;
        _midiButton.FlatStyle = FlatStyle.Flat;
        _midiButton.FlatAppearance.BorderSize = 0;
        _midiButton.Location = new Point(32, 182);
        _midiButton.Size = new Size(214, 44);
        _midiButton.Click += async (_, _) => await ToggleMidiAsync();

        var instructionsTitle = new Label
        {
            Text = "One app — no loopMIDI or separate bridge",
            Font = new Font("Segoe UI Semibold", 11.5F),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(32, 260)
        };
        var instructions = new Label
        {
            Text = "Keep this app open while using Bluetooth MIDI. It connects directly to WU-BT10 MIDI, forwards MIDI in both directions, and creates a temporary Windows MIDI Services port named “WU-BT10 MIDI Bridge”. Stop the bridge before connecting another phone, tablet, or MIDI utility.",
            ForeColor = Color.FromArgb(70, 79, 92),
            AutoSize = false,
            Location = new Point(32, 292),
            Size = new Size(680, 88)
        };
        var compatibility = new LinkLabel
        {
            Text = "Requires Windows MIDI Services — download or install it",
            ForeColor = Color.FromArgb(112, 120, 132),
            LinkColor = Color.FromArgb(0, 102, 170),
            ActiveLinkColor = Cyan,
            AutoSize = true,
            Location = new Point(32, 405)
        };
        compatibility.LinkClicked += (_, _) => OpenUrl(WindowsMidiServicesUrl);

        var midiPrivacy = new Label
        {
            Text = "• MIDI in/out • No telemetry",
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(410, 405)
        };

        page.Controls.AddRange([card, _midiButton, instructionsTitle, instructions, compatibility, midiPrivacy]);
        return page;
    }

    private async Task ToggleMidiAsync()
    {
        DiagnosticTrace.Write("ui", "midi-toggle-clicked", new Dictionary<string, object?>
        {
            ["isRunning"] = _midiBridge.IsRunning,
            ["state"] = _midiBridge.State
        });
        _midiButton.Enabled = false;
        try
        {
            if (_midiBridge.IsRunning)
            {
                await _midiBridge.StopAsync();
                SetMidiPresentation("Bluetooth MIDI bridge is stopped", "Start it again when you want to use Piano Marvel or another MIDI application.", Amber, false);
            }
            else
            {
                await Task.Run(() => RepairEngine.TraceAudioTransitionSnapshot("midi-start-requested"));
                _midiReceivedCount = 0;
                _midiSentCount = 0;
                SetMidiPresentation("Starting Bluetooth MIDI", "Preparing the virtual port and waiting for a live WU-BT10 MIDI signal…", Cyan, true);
                await _midiBridge.StartAsync();
            }
        }
        catch (Exception ex)
        {
            RepairLog.Write("MIDI bridge start failed: " + ex);
            SetMidiPresentation("MIDI bridge could not start", ex.Message, Color.Firebrick, false);
        }
        finally
        {
            _midiButton.Enabled = true;
        }
    }

    private void OnMidiStatusChanged(string message)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _midiStatusMessage.Text = message);
    }

    private void OnMidiStateChanged(MidiBridgeState state)
    {
        DiagnosticTrace.Write("ui", "midi-state-presented", new Dictionary<string, object?> { ["state"] = state });
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            switch (state)
            {
                case MidiBridgeState.Connecting:
                    SetMidiPresentation("Connecting WU-BT10 MIDI", "Waiting for the live BLE-MIDI signal and opening its GATT service…", Cyan, true);
                    break;
                case MidiBridgeState.Connected:
                    SetMidiPresentation("WU-BT10 MIDI is connected", $"Select “{WindowsMidiLoopback.PortName}” as the MIDI input and output in your MIDI application.", Green, true);
                    break;
                case MidiBridgeState.Recovering:
                    SetMidiPresentation("MIDI connection lost — reconnecting", "Keep the piano on. The bridge will rediscover WU-BT10 and restore notifications automatically.", Amber, true);
                    break;
                case MidiBridgeState.Stopping:
                    SetMidiPresentation("Disconnecting WU-BT10 MIDI", "Releasing Bluetooth and the temporary MIDI endpoint…", Amber, true);
                    break;
            }
        });
    }

    private void OnMidiMessageReceived(int count)
    {
        _midiReceivedCount = count;
        UpdateMidiActivityTitle(count);
    }

    private void OnMidiMessageSent(int count)
    {
        _midiSentCount = count;
        UpdateMidiActivityTitle(count);
    }

    private void UpdateMidiActivityTitle(int count)
    {
        if (IsDisposed || count % 10 != 1)
            return;
        BeginInvoke(() =>
        {
            var title = $"MIDI mode — {_midiReceivedCount:N0} in / {_midiSentCount:N0} out";
            _midiStatusTitle.Text = title;
            _statusTitle.Text = title;
        });
    }

    private void SetMidiPresentation(string title, string message, Color color, bool running)
    {
        _midiStatusTitle.Text = title;
        _midiStatusMessage.Text = message;
        _midiStatusMark.ForeColor = color;
        _midiButton.Text = running ? "Disconnect WU-BT10 MIDI" : "Connect WU-BT10 MIDI";
        _midiButton.BackColor = running ? Color.FromArgb(75, 85, 99) : Cyan;
        _statusTitle.Text = title;
        _statusMessage.Text = message;
        _statusMark.ForeColor = color;
        _progress.Visible = color == Cyan || color == Amber && running;
        _audioConnectButton.Text = running ? "Switch to Audio" : "Connect Audio";
        _audioConnectButton.BackColor = Cyan;
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        DiagnosticTrace.Write("ui", "form-closing", new Dictionary<string, object?>
        {
            ["isRunning"] = _midiBridge.IsRunning,
            ["closingAfterMidiStop"] = _closingAfterMidiStop,
            ["closeReason"] = e.CloseReason
        });
        if (!_closingAfterMidiStop)
        {
            e.Cancel = true;
            _closingAfterMidiStop = true;
            try
            {
                await _midiBridge.DisposeAsync();
            }
            catch (Exception ex)
            {
                DiagnosticTrace.Write("ui", "midi-dispose-on-close-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
                RepairLog.Write("MIDI bridge shutdown failed while closing: " + ex);
            }
            finally
            {
                Close();
            }
            return;
        }

        base.OnFormClosing(e);
    }

    private async Task RefreshDiagnosisAsync()
    {
        if (_refreshInProgress)
            return;

        _refreshInProgress = true;
        try
        {
            SetBusy(true, "Checking Windows audio", "Looking for WU-BT10 AUDIO and its playback endpoint…");
            var diagnosis = await Task.Run(RepairEngine.Diagnose);
            _lastAudioRefresh = DateTime.UtcNow;
            ApplyDiagnosis(diagnosis);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async Task RepairAsync()
    {
        var diagnosis = await Task.Run(RepairEngine.Diagnose);
        if (diagnosis.State is not (
                DeviceReadiness.NeedsRepair
                or DeviceReadiness.NeedsCompatibilityMode
                or DeviceReadiness.ReadyCompatibility
                or DeviceReadiness.CompatibilityDisconnected))
        {
            ApplyDiagnosis(diagnosis);
            return;
        }

        var enableCompatibility = diagnosis.State == DeviceReadiness.NeedsCompatibilityMode;
        var restoreCompatibility = diagnosis.State is DeviceReadiness.ReadyCompatibility or DeviceReadiness.CompatibilityDisconnected;
        var confirmation = MessageBox.Show(
            this,
            enableCompatibility
                ? "Testing on this PC confirmed that Intel Bluetooth-audio offload connects WU-BT10 without creating playback.\n\n" +
                  "Compatibility mode needs administrator approval to disable only the exact Intel Bluetooth Audio offload device. Windows then uses its standard A2DP path. This affects Bluetooth audio power offload system-wide, remains active across restarts, and is fully reversible from this app. It does not change profiles, services, pairing, or volume. Continue?"
                : restoreCompatibility
                    ? "Restore Intel Bluetooth-audio offload to its Windows default?\n\nWU-BT10 may again connect without a playback output on this driver. This action only re-enables the exact device previously disabled by compatibility mode."
                    : "Windows reports that Intel Bluetooth Audio is disabled but this app did not record it as compatibility mode.\n\nThis emergency action needs administrator approval to re-enable only that exact device. It will not change Bluetooth profiles or restart services. Continue?",
            enableCompatibility
                ? "Enable tested Audio compatibility mode?"
                : restoreCompatibility
                    ? "Restore Intel default mode?"
                    : "Re-enable disabled Bluetooth Audio device?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
            return;

        if (_midiBridge.IsRunning)
        {
            try
            {
                await _midiBridge.StopAsync();
            }
            catch (Exception ex)
            {
                DiagnosticTrace.Write("ui", "midi-stop-before-repair-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
                SetBusy(false, "Repair was not started", "The MIDI bridge did not stop cleanly. Disconnect MIDI and try the emergency repair again.", Color.Firebrick);
                return;
            }
        }

        SetBusy(
            true,
            "Waiting for administrator approval",
            enableCompatibility
                ? "Approve the Windows prompt to enable tested Audio compatibility mode."
                : restoreCompatibility
                    ? "Approve the Windows prompt to restore Intel default mode."
                    : "Approve the Windows prompt to enable the disabled Bluetooth Audio device.");
        var action = enableCompatibility
            ? "--enable-audio-compatibility"
            : restoreCompatibility
                ? "--restore-intel-audio"
                : "--repair";
        var result = await RepairLauncher.RunElevatedAsync(action);
        if (!result.Success)
        {
            SetBusy(false, "Repair was not completed", result.Message, Amber);
            _repairButton.Enabled = true;
            return;
        }

        if (enableCompatibility && !result.EndpointPresent)
        {
            SetBusy(
                false,
                "Audio compatibility mode is active",
                "Press Connect WU-BT10 AUDIO. The app will establish the A2DP service and set its volume to 20%.",
                Green);
            return;
        }

        await RefreshDiagnosisAsync();
    }

    private async Task ReleaseAudioForMidiHandoffAsync(CancellationToken cancellationToken)
    {
        if (!await Task.Run(RepairEngine.IsAudioEndpointActive, cancellationToken))
            return;

        DiagnosticTrace.Write("ui", "coordinated-audio-release-started");
        var released = await AudioConnectionController.DisconnectAsync(cancellationToken);
        if (!released.Success)
            throw new InvalidOperationException(released.Message);
        DiagnosticTrace.Write("ui", "coordinated-audio-release-completed");
    }

    private async Task ToggleModeAsync()
    {
        DiagnosticTrace.Write("ui", "primary-mode-toggle-clicked", new Dictionary<string, object?>
        {
            ["midiIsRunning"] = _midiBridge.IsRunning,
            ["midiState"] = _midiBridge.State,
            ["audioEndpointActive"] = RepairEngine.IsAudioEndpointActive()
        });
        _audioConnectButton.Enabled = false;
        try
        {
            if (_midiBridge.IsRunning)
            {
                SetBusy(true, "Switching to Audio", "Releasing MIDI cleanly before reconnecting the Audio profile…");
                await _midiBridge.StopAsync();

                using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    _ = await MidiBridgeEngine.FindAdvertisingWuBt10Async(
                        TimeSpan.FromSeconds(15),
                        releaseTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        "The piano did not become available after MIDI stopped. Turn on its wireless function and try again.");
                }

                var connectedAfterMidi = await AudioConnectionController.ConnectAsync();
                if (!connectedAfterMidi.Success)
                    throw new InvalidOperationException(connectedAfterMidi.Message);

                await RefreshDiagnosisAsync();
                SetBusy(false, "Audio mode is ready", "WU-BT10 AUDIO is connected and Windows volume is verified at 20%.", Green);
                return;
            }

            if (await Task.Run(RepairEngine.IsAudioEndpointActive))
            {
                _midiReceivedCount = 0;
                _midiSentCount = 0;
                SetBusy(true, "Switching to MIDI", "Preparing a persistent BLE session before Audio is released…");
                await _midiBridge.StartAsync();
                SetMidiPresentation(
                    "MIDI mode is ready",
                    $"Select “{WindowsMidiLoopback.PortName}” in your MIDI application.",
                    Green,
                    true);
                return;
            }

            SetBusy(true, "Connecting Audio", "Requesting WU-BT10 AUDIO and enforcing the 20% safety limit…");
            var connected = await AudioConnectionController.ConnectAsync();
            if (!connected.Success)
                throw new InvalidOperationException(connected.Message);

            await RefreshDiagnosisAsync();
            SetBusy(false, "Audio mode is ready", "WU-BT10 AUDIO is connected and Windows volume is verified at 20%.", Green);
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("ui", "mode-switch-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            RepairLog.Write("Could not switch WU-BT10 mode: " + ex);
            SetBusy(false,
                "Could not complete the switch",
                ex.Message,
                Color.Firebrick);
        }
        finally
        {
            _audioConnectButton.Enabled = true;
            UpdatePrimaryAction();
        }
    }

    private void UpdatePrimaryAction()
    {
        if (_midiBridge.IsRunning)
        {
            _audioConnectButton.Text = "Switch to Audio";
            _audioConnectButton.BackColor = Cyan;
            return;
        }

        _audioConnectButton.Text = RepairEngine.IsAudioEndpointActive()
            ? "Switch to MIDI"
            : "Connect Audio";
        _audioConnectButton.BackColor = Cyan;
    }

    private void ApplyDiagnosis(DiagnosticResult diagnosis)
    {
        if (_midiBridge.IsRunning)
        {
            SetMidiPresentation(
                "MIDI mode is ready",
                $"Select “{WindowsMidiLoopback.PortName}” in your MIDI application. {_midiReceivedCount:N0} messages received, {_midiSentCount:N0} sent.",
                Green,
                true);
            return;
        }

        _progress.Visible = false;
        _statusTitle.Text = diagnosis.Title;
        _statusMessage.Text = diagnosis.Message;

        var presentation = diagnosis.State switch
        {
            DeviceReadiness.Ready => (Green, false, true),
            DeviceReadiness.ReadyCompatibility => (Green, true, true),
            DeviceReadiness.ReadyDisconnected => (Green, false, false),
            DeviceReadiness.NeedsRepair => (Amber, true, false),
            DeviceReadiness.NeedsCompatibilityMode => (Amber, false, false),
            DeviceReadiness.CompatibilityDisconnected => (Green, true, false),
            DeviceReadiness.ManualAction => (Amber, false, false),
            DeviceReadiness.NotPaired => (Amber, false, false),
            DeviceReadiness.UnsupportedChipset => (Amber, false, false),
            DeviceReadiness.Error => (Color.Firebrick, false, false),
            _ => (Cyan, false, false)
        };

        _statusMark.ForeColor = presentation.Item1;
        _audioConnectButton.Enabled = true;
        _audioConnectButton.Text = presentation.Item3
            ? "Switch to MIDI"
            : "Connect Audio";
        _audioConnectButton.BackColor = _audioConnectButton.Enabled ? Cyan : Color.FromArgb(208, 213, 220);
        _audioConnectButton.ForeColor = _audioConnectButton.Enabled ? Color.White : Color.FromArgb(105, 112, 122);
        _repairButton.Enabled = presentation.Item2;
        _repairButton.Text = diagnosis.State switch
        {
            DeviceReadiness.ReadyCompatibility or DeviceReadiness.CompatibilityDisconnected => "Advanced: restore Intel default",
            _ => "Advanced recovery: re-enable device"
        };
        _repairButton.Visible = presentation.Item2;
        _repairButton.BackColor = Color.Transparent;
        _repairButton.ForeColor = presentation.Item2 ? Color.FromArgb(92, 101, 114) : Color.FromArgb(150, 156, 164);
    }

    private void SetBusy(bool busy, string title, string message, Color? color = null)
    {
        _progress.Visible = busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _statusTitle.Text = title;
        _statusMessage.Text = message;
        _statusMark.ForeColor = color ?? Cyan;
        _repairButton.Enabled = !busy;
        _audioConnectButton.Enabled = !busy;
        _repairButton.BackColor = Color.Transparent;
        _repairButton.ForeColor = busy ? Color.FromArgb(150, 156, 164) : Color.FromArgb(92, 101, 114);
    }

    private static Bitmap? LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WUBT10Fix.app-icon.png");
        return stream is null ? null : new Bitmap(stream);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not open URL: " + ex);
        }
    }

    private static void OpenLog()
    {
        try
        {
            var path = DiagnosticTrace.CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                DiagnosticTrace.Write("ui", "diagnostic-log-open-requested");
                path = DiagnosticTrace.CurrentPath;
            }

            if (!string.IsNullOrWhiteSpace(path))
                Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("ui", "diagnostic-log-open-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            RepairLog.Write("Could not open log: " + ex);
        }
    }
}
