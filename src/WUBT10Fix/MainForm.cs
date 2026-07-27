using System.Diagnostics;
using System.Reflection;

namespace WUBT10Fix;

internal sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(11, 31, 63);
    private static readonly Color Cyan = Color.FromArgb(0, 174, 214);
    private static readonly Color Green = Color.FromArgb(22, 137, 82);
    private static readonly Color Amber = Color.FromArgb(184, 115, 0);
    private static readonly Color Muted = Color.FromArgb(70, 79, 92);
    private static readonly Color SoftBackground = Color.FromArgb(246, 248, 251);

    private readonly Label _audioStatusTitle = new();
    private readonly Label _audioStatusMessage = new();
    private readonly Label _audioStatusMark = new();
    private readonly Button _audioButton = new();
    private readonly Button _repairButton = new();
    private readonly Label _midiStatusTitle = new();
    private readonly Label _midiStatusMessage = new();
    private readonly Label _midiStatusMark = new();
    private readonly Button _midiButton = new();
    private readonly Button _logButton = new();
    private readonly ProgressBar _progress = new();
    private readonly SemaphoreSlim _uiOperations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MidiBridgeEngine _midiBridge;
    private readonly ConnectionCoordinator _connections;
    private bool _closingAfterCleanup;
    private bool _refreshInProgress;
    private bool _audioConnected;
    private int _midiReceivedCount;
    private int _midiSentCount;
    private DateTime _lastAudioRefresh = DateTime.MinValue;

    internal MainForm()
    {
        _midiBridge = new MidiBridgeEngine(
            maintainConnection: true,
            afterMaintainConnection: ReleaseAudioForMidiHandoffAsync);
        _connections = new ConnectionCoordinator(
            RepairEngine.IsAudioConnectionPresent,
            () => _midiBridge.IsRunning,
            AudioConnectionController.ConnectAsync,
            AudioConnectionController.DisconnectAsync,
            _midiBridge.StartAsync,
            _midiBridge.StopAsync,
            WaitForMidiAdvertisementAsync);

        Text = "WU-BT10 Windows Fix";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 650);
        ClientSize = new Size(760, 720);
        BackColor = SoftBackground;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        _midiBridge.StatusChanged += OnMidiStatusChanged;
        _midiBridge.MessageReceived += OnMidiMessageReceived;
        _midiBridge.MessageSent += OnMidiMessageSent;
        _midiBridge.StateChanged += OnMidiStateChanged;
        Shown += async (_, _) =>
        {
            DiagnosticTrace.Write("ui", "startup-read-only-diagnosis");
            await RefreshDiagnosisAsync(showProgress: true);
        };
        Activated += async (_, _) =>
        {
            if (!_closingAfterCleanup
                && DateTime.UtcNow - _lastAudioRefresh > TimeSpan.FromSeconds(2)
                && _uiOperations.CurrentCount > 0)
            {
                await RefreshDiagnosisAsync(showProgress: false);
            }
        };
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Width = ClientSize.Width,
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
            Text = "Restore missing Casio Audio and MIDI capabilities on Windows.",
            ForeColor = Color.FromArgb(203, 220, 239),
            Font = new Font("Segoe UI", 10.5F),
            AutoSize = true,
            Location = new Point(128, 64)
        };

        var supportOnceButton = BuildSupportButton(
            "SupportOnceButton",
            "Once $10+",
            "Support once — $10 or more",
            "Open secure one-time support page. You can choose any amount from $10.",
            new Point(500, 20),
            new Size(108, 34));
        supportOnceButton.Click += (_, _) => OpenUrl(ExternalLinks.SupportOnce);

        var supportMonthlyButton = BuildSupportButton(
            "SupportMonthlyButton",
            "Monthly $5+",
            "Support monthly — $5 or more",
            "Open secure monthly support page. You can choose any amount from $5 per month.",
            new Point(616, 20),
            new Size(116, 34));
        supportMonthlyButton.Click += (_, _) => OpenUrl(ExternalLinks.SupportMonthly);

        header.Controls.AddRange([
            logo,
            title,
            subtitle,
            supportOnceButton,
            supportMonthlyButton
        ]);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 24, 32, 20),
            AutoScroll = true
        };

        var audioCard = BuildAudioCard();
        var midiCard = BuildMidiCard();

        _progress.Location = new Point(32, 406);
        _progress.Size = new Size(696, 7);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 25;
        _progress.Visible = false;

        var switchingTitle = new Label
        {
            Text = "Before you connect",
            Font = new Font("Segoe UI Semibold", 11.5F),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(32, 428)
        };
        var switchingDetails = new LinkLabel
        {
            Text = "1. Install Windows MIDI Services and restart Windows.\r\n"
                + "2. Choose Audio or MIDI and the app handles the connection automatically.\r\n"
                + "   Note: Audio is kept at 20% for speaker safety.",
            ForeColor = Muted,
            LinkColor = Color.FromArgb(0, 102, 170),
            ActiveLinkColor = Cyan,
            LinkArea = new LinkArea(
                "1. Install ".Length,
                "Windows MIDI Services".Length),
            AutoSize = false,
            Location = new Point(32, 457),
            Size = new Size(696, 64),
            AccessibleName = "Windows MIDI Services setup and connection guidance"
        };
        switchingDetails.LinkClicked += (_, _) => OpenUrl(ExternalLinks.WindowsMidiServices);

        _logButton.Name = "DiagnosticsButton";
        _logButton.AccessibleName = "Open diagnostics";
        _logButton.Text = "Diagnostics";
        _logButton.FlatStyle = FlatStyle.Flat;
        _logButton.ForeColor = Navy;
        _logButton.Location = new Point(32, 537);
        _logButton.Size = new Size(112, 32);
        _logButton.Click += (_, _) => OpenLog();

        var privacyText = new Label
        {
            Text = $"No telemetry • Standard-user switching • Version {Application.ProductVersion.Split('+')[0]}",
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(32, 586)
        };

        content.Controls.AddRange([
            audioCard,
            midiCard,
            _progress,
            switchingTitle,
            switchingDetails,
            _logButton,
            privacyText
        ]);
        Controls.Add(content);
        Controls.Add(header);
    }

    private Panel BuildAudioCard()
    {
        var card = new Panel
        {
            Name = "AudioSection",
            AccessibleName = "Audio connection",
            Location = new Point(32, 24),
            Size = new Size(696, 164),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var heading = new Label
        {
            Text = "AUDIO",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(20, 14)
        };

        ConfigureStatusMark(_audioStatusMark, new Point(20, 38), Cyan);
        ConfigureStatusTitle(_audioStatusTitle, "Checking WU-BT10 AUDIO", new Point(68, 37));
        ConfigureStatusMessage(
            _audioStatusMessage,
            "Reading the current Windows Audio state. This does not connect or change anything.",
            new Point(70, 70),
            new Size(598, 42));

        ConfigureModeButton(_audioButton, "AudioConnectButton", "Connect Audio", new Point(70, 110));
        _audioButton.Click += async (_, _) => await RunUiOperationAsync(HandleAudioButtonAsync);

        _repairButton.Name = "AudioRecoveryButton";
        _repairButton.AccessibleName = "Advanced Audio recovery";
        _repairButton.Text = "Advanced Audio recovery";
        _repairButton.Font = new Font("Segoe UI", 9F);
        _repairButton.BackColor = Color.Transparent;
        _repairButton.ForeColor = Color.FromArgb(92, 101, 114);
        _repairButton.FlatStyle = FlatStyle.Flat;
        _repairButton.FlatAppearance.BorderColor = Color.FromArgb(208, 213, 220);
        _repairButton.Location = new Point(430, 113);
        _repairButton.Size = new Size(238, 32);
        _repairButton.Visible = false;
        _repairButton.Click += async (_, _) => await RunUiOperationAsync(_ => RepairAsync());

        card.Controls.AddRange([
            heading,
            _audioStatusMark,
            _audioStatusTitle,
            _audioStatusMessage,
            _audioButton,
            _repairButton
        ]);
        return card;
    }

    private Panel BuildMidiCard()
    {
        var card = new Panel
        {
            Name = "MidiSection",
            AccessibleName = "MIDI connection",
            Location = new Point(32, 206),
            Size = new Size(696, 182),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var heading = new Label
        {
            Text = "MIDI",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(20, 14)
        };

        ConfigureStatusMark(_midiStatusMark, new Point(20, 38), Amber);
        ConfigureStatusTitle(_midiStatusTitle, "WU-BT10 MIDI is disconnected", new Point(68, 37));
        ConfigureStatusMessage(
            _midiStatusMessage,
            $"Connect, then select “{WindowsMidiLoopback.PortName}” in your MIDI application.",
            new Point(70, 70),
            new Size(598, 32));

        var requirements = new LinkLabel
        {
            Text = "Install Windows MIDI Services and restart Windows before connecting MIDI.",
            ForeColor = Muted,
            LinkColor = Color.FromArgb(0, 102, 170),
            ActiveLinkColor = Cyan,
            LinkArea = new LinkArea(
                "Install ".Length,
                "Windows MIDI Services".Length),
            AutoSize = false,
            Location = new Point(70, 101),
            Size = new Size(598, 22),
            AccessibleName = "Install Windows MIDI Services and restart Windows before connecting MIDI"
        };
        requirements.LinkClicked += (_, _) => OpenUrl(ExternalLinks.WindowsMidiServices);

        ConfigureModeButton(_midiButton, "MidiConnectButton", "Connect MIDI", new Point(70, 128));
        _midiButton.Click += async (_, _) => await RunUiOperationAsync(HandleMidiButtonAsync);

        card.Controls.AddRange([
            heading,
            _midiStatusMark,
            _midiStatusTitle,
            _midiStatusMessage,
            requirements,
            _midiButton
        ]);
        return card;
    }

    private static void ConfigureStatusMark(Label label, Point location, Color color)
    {
        label.Text = "●";
        label.Font = new Font("Segoe UI", 24F);
        label.ForeColor = color;
        label.AutoSize = true;
        label.Location = location;
    }

    private static void ConfigureStatusTitle(Label label, string text, Point location)
    {
        label.Text = text;
        label.Font = new Font("Segoe UI Semibold", 13F);
        label.ForeColor = Navy;
        label.AutoSize = false;
        label.Location = location;
        label.Size = new Size(600, 30);
    }

    private static void ConfigureStatusMessage(Label label, string text, Point location, Size size)
    {
        label.Text = text;
        label.ForeColor = Muted;
        label.AutoSize = false;
        label.Location = location;
        label.Size = size;
    }

    private static void ConfigureModeButton(Button button, string name, string text, Point location)
    {
        button.Name = name;
        button.AccessibleName = text;
        button.Text = text;
        button.Font = new Font("Segoe UI Semibold", 10.5F);
        button.BackColor = Cyan;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Location = location;
        button.Size = new Size(210, 40);
    }

    private static Button BuildSupportButton(
        string name,
        string text,
        string accessibleName,
        string accessibleDescription,
        Point location,
        Size size)
    {
        var button = new Button
        {
            Name = name,
            AccessibleName = accessibleName,
            AccessibleDescription = accessibleDescription,
            Text = text,
            Font = new Font("Segoe UI Semibold", 9F),
            BackColor = Green,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = location,
            Size = size,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private async Task RunUiOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (!await _uiOperations.WaitAsync(0))
            return;

        SetControlsBusy(true);
        try
        {
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            DiagnosticTrace.Write("ui", "connection-operation-cancelled-for-close");
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("ui", "connection-operation-failed", new Dictionary<string, object?>
            {
                ["exception"] = ex.ToString()
            });
            RepairLog.Write("Connection operation failed: " + ex);
            PresentOperationError(ex.Message);
        }
        finally
        {
            SetControlsBusy(false);
            _uiOperations.Release();
        }
    }

    private async Task HandleAudioButtonAsync(CancellationToken cancellationToken)
    {
        DiagnosticTrace.Write("ui", "audio-button-clicked", new Dictionary<string, object?>
        {
            ["audioConnected"] = _audioConnected,
            ["midiRunning"] = _midiBridge.IsRunning
        });
        if (_audioConnected)
        {
            SetAudioPresentation(
                "Disconnecting WU-BT10 AUDIO",
                "Releasing only the Audio profile while preserving pairing.",
                Amber,
                connected: true);
            var result = await _connections.DisconnectAudioAsync(cancellationToken);
            SetAudioPresentation("WU-BT10 AUDIO is disconnected", result.Message, Amber, connected: false);
        }
        else
        {
            SetAudioPresentation(
                _midiBridge.IsRunning ? "Switching from MIDI to Audio" : "Connecting WU-BT10 AUDIO",
                _midiBridge.IsRunning
                    ? "Releasing MIDI cleanly, confirming WU-BT10 is available, then connecting Audio at 20%."
                    : "Connecting the preserved Windows Audio endpoint and verifying the 20% safety limit.",
                Cyan,
                connected: false);
            var result = await _connections.ConnectAudioAsync(cancellationToken);
            SetAudioPresentation("WU-BT10 AUDIO is connected", result.Message, Green, connected: true);
        }

        await RefreshDiagnosisAsync(showProgress: false);
    }

    private async Task HandleMidiButtonAsync(CancellationToken cancellationToken)
    {
        DiagnosticTrace.Write("ui", "midi-button-clicked", new Dictionary<string, object?>
        {
            ["audioConnected"] = _audioConnected,
            ["midiRunning"] = _midiBridge.IsRunning,
            ["midiState"] = _midiBridge.State
        });
        if (_midiBridge.IsRunning)
        {
            SetMidiPresentation(
                "Disconnecting WU-BT10 MIDI",
                "Unsubscribing notifications, releasing BLE, and checking that WU-BT10 advertises again.",
                Amber,
                running: true);
            var result = await _connections.DisconnectMidiAsync(cancellationToken);
            SetMidiPresentation("WU-BT10 MIDI is disconnected", result.Message, Amber, running: false);
        }
        else
        {
            _midiReceivedCount = 0;
            _midiSentCount = 0;
            SetMidiPresentation(
                _audioConnected ? "Switching from Audio to MIDI" : "Connecting WU-BT10 MIDI",
                _audioConnected
                    ? "Preparing the BLE session before releasing Audio, then opening the MIDI service."
                    : "Creating the temporary Windows MIDI port and opening the live BLE-MIDI service.",
                Cyan,
                running: true);
            var result = await _connections.ConnectMidiAsync(cancellationToken);
            SetMidiPresentation("WU-BT10 MIDI is connected", result.Message, Green, running: true);
        }

        await RefreshDiagnosisAsync(showProgress: false);
    }

    private Task ReleaseAudioForMidiHandoffAsync(CancellationToken cancellationToken) =>
        _connections.ReleaseAudioForMidiHandoffAsync(cancellationToken);

    private static async Task WaitForMidiAdvertisementAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            _ = await MidiBridgeEngine.FindAdvertisingWuBt10Async(TimeSpan.FromSeconds(20), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "MIDI was released, but WU-BT10 did not advertise again within 20 seconds. " +
                "Make sure the piano wireless function is on, then try again.");
        }
    }

    private void OnMidiStatusChanged(string message)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(() => _midiStatusMessage.Text = message);
    }

    private void OnMidiStateChanged(MidiBridgeState state)
    {
        DiagnosticTrace.Write("ui", "midi-state-presented", new Dictionary<string, object?> { ["state"] = state });
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            switch (state)
            {
                case MidiBridgeState.Stopped:
                    SetMidiPresentation(
                        "WU-BT10 MIDI is disconnected",
                        $"Connect, then select “{WindowsMidiLoopback.PortName}” in your MIDI application.",
                        Amber,
                        running: false);
                    break;
                case MidiBridgeState.Connecting:
                    SetMidiPresentation(
                        "Connecting WU-BT10 MIDI",
                        "Waiting for the live BLE-MIDI signal and opening its GATT service.",
                        Cyan,
                        running: true);
                    break;
                case MidiBridgeState.Connected:
                    SetMidiPresentation(
                        "WU-BT10 MIDI is connected",
                        $"Select “{WindowsMidiLoopback.PortName}” as MIDI input and output.",
                        Green,
                        running: true);
                    break;
                case MidiBridgeState.Recovering:
                    SetMidiPresentation(
                        "MIDI connection lost — reconnecting",
                        "Keep the piano on. The bridge is rediscovering WU-BT10 automatically.",
                        Amber,
                        running: true);
                    break;
                case MidiBridgeState.Stopping:
                    SetMidiPresentation(
                        "Disconnecting WU-BT10 MIDI",
                        "Releasing Bluetooth and the temporary MIDI endpoint.",
                        Amber,
                        running: true);
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
        if (IsDisposed || !IsHandleCreated || count % 10 != 1)
            return;
        BeginInvoke(() => _midiStatusTitle.Text = $"WU-BT10 MIDI — {_midiReceivedCount:N0} in / {_midiSentCount:N0} out");
    }

    private async Task RefreshDiagnosisAsync(bool showProgress)
    {
        if (_refreshInProgress || _closingAfterCleanup)
            return;

        _refreshInProgress = true;
        if (showProgress)
        {
            _progress.Visible = true;
            _audioStatusTitle.Text = "Checking WU-BT10 AUDIO";
            _audioStatusMessage.Text = "Reading the current Windows state. No connection is attempted.";
            _audioStatusMark.ForeColor = Cyan;
        }

        try
        {
            var diagnosis = await Task.Run(RepairEngine.Diagnose);
            _audioConnected = await Task.Run(RepairEngine.IsAudioConnectionPresent);
            _lastAudioRefresh = DateTime.UtcNow;
            ApplyDiagnosis(diagnosis);
        }
        finally
        {
            if (showProgress)
                _progress.Visible = false;
            _refreshInProgress = false;
        }
    }

    private void ApplyDiagnosis(DiagnosticResult diagnosis)
    {
        _audioStatusTitle.Text = diagnosis.Title;
        _audioStatusMessage.Text = diagnosis.Message;

        var presentation = diagnosis.State switch
        {
            DeviceReadiness.Ready => (Green, false),
            DeviceReadiness.ReadyCompatibility => (Green, true),
            DeviceReadiness.ReadyDisconnected => (Amber, false),
            DeviceReadiness.NeedsRepair => (Amber, true),
            DeviceReadiness.NeedsCompatibilityMode => (Amber, true),
            DeviceReadiness.CompatibilityDisconnected => (Amber, true),
            DeviceReadiness.ManualAction => (Amber, false),
            DeviceReadiness.NotPaired => (Amber, false),
            DeviceReadiness.UnsupportedChipset => (Amber, false),
            DeviceReadiness.Error => (Color.Firebrick, false),
            _ => (Cyan, false)
        };

        _audioStatusMark.ForeColor = presentation.Item1;
        _audioButton.Text = _audioConnected ? "Disconnect Audio" : "Connect Audio";
        _audioButton.AccessibleName = _audioButton.Text;
        _repairButton.Enabled = presentation.Item2;
        _repairButton.Text = diagnosis.State switch
        {
            DeviceReadiness.ReadyCompatibility or DeviceReadiness.CompatibilityDisconnected =>
                "Advanced: restore Intel default",
            DeviceReadiness.NeedsCompatibilityMode => "Advanced: enable compatibility",
            _ => "Advanced recovery: re-enable device"
        };
        _repairButton.Visible = presentation.Item2 || diagnosis.State == DeviceReadiness.NeedsCompatibilityMode;
    }

    private void SetAudioPresentation(string title, string message, Color color, bool connected)
    {
        _audioStatusTitle.Text = title;
        _audioStatusMessage.Text = message;
        _audioStatusMark.ForeColor = color;
        _audioConnected = connected;
        _audioButton.Text = connected ? "Disconnect Audio" : "Connect Audio";
        _audioButton.AccessibleName = _audioButton.Text;
    }

    private void SetMidiPresentation(string title, string message, Color color, bool running)
    {
        _midiStatusTitle.Text = title;
        _midiStatusMessage.Text = message;
        _midiStatusMark.ForeColor = color;
        _midiButton.Text = running ? "Disconnect MIDI" : "Connect MIDI";
        _midiButton.AccessibleName = _midiButton.Text;
    }

    private void SetControlsBusy(bool busy)
    {
        _progress.Visible = busy;
        _audioButton.Enabled = !busy;
        _midiButton.Enabled = !busy;
        _repairButton.Enabled = !busy && _repairButton.Visible;
        _audioButton.BackColor = busy ? Color.FromArgb(208, 213, 220) : Cyan;
        _midiButton.BackColor = busy ? Color.FromArgb(208, 213, 220) : Cyan;
    }

    private void PresentOperationError(string message)
    {
        _audioConnected = RepairEngine.IsAudioConnectionPresent();
        _audioButton.Text = _audioConnected ? "Disconnect Audio" : "Connect Audio";
        _audioButton.AccessibleName = _audioButton.Text;
        if (_midiBridge.IsRunning)
        {
            SetMidiPresentation("MIDI operation could not complete", message, Color.Firebrick, running: true);
        }
        else if (_audioConnected)
        {
            SetAudioPresentation("Audio operation could not complete", message, Color.Firebrick, connected: true);
        }
        else
        {
            _audioStatusMark.ForeColor = Color.Firebrick;
            _audioStatusTitle.Text = "Connection operation could not complete";
            _audioStatusMessage.Text = message;
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
                  "Compatibility mode needs administrator approval to disable only the exact Intel Bluetooth Audio offload device. Windows then uses its standard A2DP path. This affects Bluetooth audio power offload system-wide, remains active across restarts, and is fully reversible. Continue?"
                : restoreCompatibility
                    ? "Restore Intel Bluetooth-audio offload to its Windows default?\n\nWU-BT10 may again connect without a playback output on this driver."
                    : "Windows reports that Intel Bluetooth Audio is disabled but this app did not record it as compatibility mode.\n\nRe-enable only that exact device?",
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
            await _connections.DisconnectMidiAsync(_lifetime.Token);

        _audioStatusTitle.Text = "Waiting for administrator approval";
        _audioStatusMessage.Text = "Approve the Windows prompt only if the exact Audio recovery described above is expected.";
        _audioStatusMark.ForeColor = Cyan;
        var action = enableCompatibility
            ? "--enable-audio-compatibility"
            : restoreCompatibility
                ? "--restore-intel-audio"
                : "--repair";
        var result = await RepairLauncher.RunElevatedAsync(action);
        if (!result.Success)
        {
            SetAudioPresentation("Audio recovery was not completed", result.Message, Amber, _audioConnected);
            return;
        }

        await RefreshDiagnosisAsync(showProgress: false);
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        DiagnosticTrace.Write("ui", "form-closing", new Dictionary<string, object?>
        {
            ["midiRunning"] = _midiBridge.IsRunning,
            ["closingAfterCleanup"] = _closingAfterCleanup,
            ["closeReason"] = e.CloseReason
        });
        if (!_closingAfterCleanup)
        {
            e.Cancel = true;
            _closingAfterCleanup = true;
            _lifetime.Cancel();
            await _uiOperations.WaitAsync();
            try
            {
                await _midiBridge.DisposeAsync();
            }
            catch (Exception ex)
            {
                DiagnosticTrace.Write("ui", "midi-dispose-on-close-failed", new Dictionary<string, object?>
                {
                    ["exception"] = ex.ToString()
                });
                RepairLog.Write("MIDI bridge shutdown failed while closing: " + ex);
            }
            finally
            {
                _uiOperations.Release();
                _lifetime.Dispose();
                _connections.Dispose();
                _uiOperations.Dispose();
                Close();
            }
            return;
        }

        base.OnFormClosing(e);
    }

    private static Bitmap? LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WUBT10Fix.app-icon.png");
        return stream is null ? null : new Bitmap(stream);
    }

    private static void OpenUrl(string url)
    {
        _ = ExternalLinks.TryOpen(url);
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
            DiagnosticTrace.Write("ui", "diagnostic-log-open-failed", new Dictionary<string, object?>
            {
                ["exception"] = ex.ToString()
            });
            RepairLog.Write("Could not open log: " + ex);
        }
    }
}
