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
    private readonly Button _soundButton = new();
    private readonly Button _supportButton = new();
    private readonly Button _logButton = new();
    private readonly ProgressBar _progress = new();
    private readonly MidiBridgeEngine _midiBridge = new();
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
        Text = "WU-BT10 Windows Fix";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 640);
        ClientSize = new Size(780, 680);
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
            Height = 120,
            BackColor = Navy,
            Padding = new Padding(28, 18, 28, 16)
        };

        var logo = new PictureBox
        {
            Location = new Point(28, 18),
            Size = new Size(82, 82),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadLogo(),
            AccessibleName = "Wireless piano connection icon"
        };

        var title = new Label
        {
            Text = "WU-BT10 Windows Fix",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20F),
            AutoSize = true,
            Location = new Point(126, 27)
        };

        var subtitle = new Label
        {
            Text = "Repair Bluetooth audio and bridge low-latency MIDI",
            ForeColor = Color.FromArgb(203, 220, 239),
            Font = new Font("Segoe UI", 10.5F),
            AutoSize = true,
            Location = new Point(130, 70)
        };

        header.Controls.AddRange([logo, title, subtitle]);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 28, 32, 22),
            AutoScroll = true
        };

        var statusCard = new Panel
        {
            Location = new Point(32, 28),
            Size = new Size(696, 126),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _statusMark.Text = "●";
        _statusMark.Font = new Font("Segoe UI", 24F);
        _statusMark.ForeColor = Cyan;
        _statusMark.AutoSize = true;
        _statusMark.Location = new Point(22, 26);

        _statusTitle.Text = "Checking Windows audio";
        _statusTitle.Font = new Font("Segoe UI Semibold", 13F);
        _statusTitle.ForeColor = Navy;
        _statusTitle.AutoSize = false;
        _statusTitle.Location = new Point(72, 22);
        _statusTitle.Size = new Size(590, 30);

        _statusMessage.Text = "Looking for WU-BT10 AUDIO…";
        _statusMessage.ForeColor = Color.FromArgb(70, 79, 92);
        _statusMessage.AutoSize = false;
        _statusMessage.Location = new Point(75, 56);
        _statusMessage.Size = new Size(585, 55);

        statusCard.Controls.AddRange([_statusMark, _statusTitle, _statusMessage]);

        _progress.Location = new Point(32, 169);
        _progress.Size = new Size(696, 7);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 25;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        _audioConnectButton.Text = "Connect Audio in Windows";
        _audioConnectButton.Font = new Font("Segoe UI Semibold", 10.5F);
        _audioConnectButton.BackColor = Cyan;
        _audioConnectButton.ForeColor = Color.White;
        _audioConnectButton.FlatStyle = FlatStyle.Flat;
        _audioConnectButton.FlatAppearance.BorderSize = 0;
        _audioConnectButton.Location = new Point(32, 196);
        _audioConnectButton.Size = new Size(214, 44);
        _audioConnectButton.Click += async (_, _) => await ConnectAudioAsync();

        _repairButton.Text = "Emergency: re-enable device";
        _repairButton.Font = new Font("Segoe UI Semibold", 10.5F);
        _repairButton.BackColor = Cyan;
        _repairButton.ForeColor = Color.White;
        _repairButton.FlatStyle = FlatStyle.Flat;
        _repairButton.FlatAppearance.BorderSize = 0;
        _repairButton.Location = new Point(260, 196);
        _repairButton.Size = new Size(214, 44);
        _repairButton.Visible = false;
        _repairButton.Click += async (_, _) => await RepairAsync();

        _soundButton.Text = "Set WU-BT10 to 20%";
        _soundButton.Location = new Point(488, 196);
        _soundButton.Size = new Size(190, 44);
        _soundButton.Enabled = false;
        _soundButton.Click += async (_, _) => await SetSafeVolumeAsync();

        _supportButton.Text = "Support development";
        _supportButton.Location = new Point(196, 256);
        _supportButton.Size = new Size(184, 44);
        _supportButton.Visible = SupportOptions.SupportUrl is not null;
        _supportButton.Click += (_, _) => OpenUrl(SupportOptions.SupportUrl!);

        _logButton.Text = "View diagnostic log";
        _logButton.FlatStyle = FlatStyle.Flat;
        _logButton.ForeColor = Navy;
        _logButton.Location = new Point(32, 256);
        _logButton.Size = new Size(148, 34);
        _logButton.Click += (_, _) => OpenLog();

        var persistenceTitle = new Label
        {
            Text = "Safe audio handling — no Bluetooth stack resets",
            Font = new Font("Segoe UI Semibold", 11.5F),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(32, 315)
        };

        var persistenceText = new Label
        {
            Text = "Diagnosis is read-only. The app sets WU-BT10 AUDIO to 20% only when you press the volume button. Repair is enable-only: it can restore a disabled Intel Bluetooth Audio device, but never disables hardware, toggles Bluetooth profiles, or restarts Windows audio services.",
            ForeColor = Color.FromArgb(70, 79, 92),
            AutoSize = false,
            Location = new Point(32, 345),
            Size = new Size(680, 72),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var privacyText = new Label
        {
            Text = $"Open source • No network access during repair • Version {Application.ProductVersion}",
            ForeColor = Color.FromArgb(112, 120, 132),
            AutoSize = true,
            Location = new Point(32, 433)
        };

        content.Controls.AddRange([
            statusCard,
            _progress,
            _audioConnectButton,
            _repairButton,
            _soundButton,
            _supportButton,
            _logButton,
            persistenceTitle,
            persistenceText,
            privacyText
        ]);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 14F),
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(150, 52)
        };
        var audioPage = new TabPage("Audio")
        {
            BackColor = SoftBackground,
            Font = new Font("Segoe UI", 10F)
        };
        audioPage.Controls.Add(content);
        tabs.TabPages.Add(audioPage);
        tabs.TabPages.Add(BuildMidiPage());

        Controls.Add(tabs);
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
                if (await Task.Run(RepairEngine.IsAudioEndpointActive))
                {
                    var openSettings = MessageBox.Show(
                        this,
                        "WU-BT10 AUDIO is still active. Casio warns that simultaneous Audio and MIDI can be unstable.\n\nDisconnect WU-BT10 AUDIO in Windows Bluetooth settings, then press Connect MIDI again. Open Bluetooth settings now?",
                        "Disconnect Audio before MIDI",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (openSettings == DialogResult.Yes)
                        OpenUrl("ms-settings:bluetooth");
                    return;
                }

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
        BeginInvoke(() => _midiStatusTitle.Text =
            $"WU-BT10 MIDI is connected — {_midiReceivedCount:N0} in / {_midiSentCount:N0} out");
    }

    private void SetMidiPresentation(string title, string message, Color color, bool running)
    {
        _midiStatusTitle.Text = title;
        _midiStatusMessage.Text = message;
        _midiStatusMark.ForeColor = color;
        _midiButton.Text = running ? "Disconnect WU-BT10 MIDI" : "Connect WU-BT10 MIDI";
        _midiButton.BackColor = running ? Color.FromArgb(75, 85, 99) : Cyan;
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
        if (diagnosis.State != DeviceReadiness.NeedsRepair)
        {
            ApplyDiagnosis(diagnosis);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Windows reports that Intel Bluetooth Audio is already disabled.\n\n" +
            "This optional emergency action needs administrator approval because it runs only:\n" +
            "pnputil /enable-device <the exact disabled Intel Bluetooth Audio device>\n\n" +
            "It will not disable or restart devices, change Bluetooth profiles, or restart services. Continue?",
            "Re-enable disabled Bluetooth Audio device?",
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

        SetBusy(true, "Waiting for administrator approval", "Approve the Windows prompt to enable the disabled Bluetooth Audio device.");
        var result = await RepairLauncher.RunElevatedAsync();
        if (!result.Success)
        {
            SetBusy(false, "Repair was not completed", result.Message, Amber);
            _repairButton.Enabled = true;
            return;
        }

        await RefreshDiagnosisAsync();
    }

    private async Task ConnectAudioAsync()
    {
        DiagnosticTrace.Write("ui", "audio-connect-clicked", new Dictionary<string, object?>
        {
            ["midiIsRunning"] = _midiBridge.IsRunning,
            ["midiState"] = _midiBridge.State
        });
        _audioConnectButton.Enabled = false;
        try
        {
            if (_midiBridge.IsRunning)
                await _midiBridge.StopAsync();

            OpenUrl("ms-settings:bluetooth");
            DiagnosticTrace.Write("ui", "bluetooth-settings-opened-for-audio");
            SetBusy(false,
                "Connect WU-BT10 AUDIO in Windows",
                "Select WU-BT10 AUDIO in Bluetooth settings. Return here afterward; diagnosis will detect the playback endpoint without changing its volume.",
                Cyan);
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("ui", "audio-switch-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            RepairLog.Write("Could not stop MIDI before opening Audio settings: " + ex);
            SetBusy(false,
                "Could not switch safely to Audio",
                "The MIDI bridge did not stop cleanly, so Windows Bluetooth settings were not opened. Try Disconnect MIDI again, then retry Audio.",
                Color.Firebrick);
        }
        finally
        {
            _audioConnectButton.Enabled = true;
        }
    }

    private async Task SetSafeVolumeAsync()
    {
        SetBusy(true, "Setting WU-BT10 AUDIO volume", "Applying 20% only to the active WU-BT10 playback endpoint…");
        try
        {
            var result = await Task.Run(() => RepairEngine.SetSafeVolume());
            if (!result.Success)
            {
                SetBusy(false, "WU-BT10 volume was not changed", result.Message, Color.Firebrick);
                return;
            }

            await RefreshDiagnosisAsync();
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("ui", "safe-volume-failed", new Dictionary<string, object?> { ["exception"] = ex.ToString() });
            RepairLog.Write("WU-BT10 volume action failed: " + ex);
            SetBusy(false, "WU-BT10 volume was not changed", "Windows could not apply the requested volume. View the diagnostic log for details.", Color.Firebrick);
        }
    }

    private void ApplyDiagnosis(DiagnosticResult diagnosis)
    {
        _progress.Visible = false;
        _statusTitle.Text = diagnosis.Title;
        _statusMessage.Text = diagnosis.Message;

        var presentation = diagnosis.State switch
        {
            DeviceReadiness.Ready => (Green, false, true),
            DeviceReadiness.ReadyDisconnected => (Green, false, false),
            DeviceReadiness.NeedsRepair => (Amber, true, false),
            DeviceReadiness.ManualAction => (Amber, false, false),
            DeviceReadiness.NotPaired => (Amber, false, false),
            DeviceReadiness.UnsupportedChipset => (Amber, false, false),
            DeviceReadiness.Error => (Color.Firebrick, true, false),
            _ => (Cyan, false, false)
        };

        _statusMark.ForeColor = presentation.Item1;
        _audioConnectButton.Enabled = true;
        _audioConnectButton.BackColor = _audioConnectButton.Enabled ? Cyan : Color.FromArgb(208, 213, 220);
        _audioConnectButton.ForeColor = _audioConnectButton.Enabled ? Color.White : Color.FromArgb(105, 112, 122);
        _repairButton.Enabled = presentation.Item2;
        _repairButton.Text = "Emergency: re-enable device";
        _repairButton.Visible = presentation.Item2;
        _repairButton.BackColor = presentation.Item2 ? Cyan : Color.FromArgb(208, 213, 220);
        _repairButton.ForeColor = presentation.Item2 ? Color.White : Color.FromArgb(105, 112, 122);
        _soundButton.Enabled = presentation.Item3;
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
        _repairButton.BackColor = busy ? Color.FromArgb(208, 213, 220) : Cyan;
        _repairButton.ForeColor = busy ? Color.FromArgb(105, 112, 122) : Color.White;
        _soundButton.Enabled = false;
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
