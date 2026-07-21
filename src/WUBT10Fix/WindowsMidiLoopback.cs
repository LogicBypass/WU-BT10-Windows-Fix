using System.Diagnostics;

namespace WUBT10Fix;

internal sealed class WindowsMidiLoopback : IDisposable
{
    internal const string PortName = "WU-BT10 MIDI Bridge";
    private const string AppPortName = "WU-BT10 MIDI Bridge (App)";
    private const string AssociationId = "{F310E87A-6AFE-4B95-A108-546038EB8449}";
    private bool _created;

    internal WinMmMidiOutput OpenOutput()
    {
        DiagnosticTrace.Write("wms", "loopback-open-started", new Dictionary<string, object?>
        {
            ["appPortName"] = AppPortName,
            ["clientPortName"] = PortName,
            ["associationId"] = AssociationId
        });
        try
        {
            var existing = WinMmMidiOutput.Open(AppPortName);
            _created = true;
            DiagnosticTrace.Write("wms", "loopback-existing-endpoint-opened", new Dictionary<string, object?>
            {
                ["appPortName"] = AppPortName,
                ["ownershipWarning"] = "Existing endpoint is reused but this instance will remove the shared association on dispose."
            });
            return existing;
        }
        catch (InvalidOperationException ex)
        {
            DiagnosticTrace.Write("wms", "loopback-existing-endpoint-not-found", new Dictionary<string, object?>
            {
                ["exception"] = ex.Message
            });
            // The temporary endpoint does not exist yet.
        }

        var midiTool = FindMidiTool();
        DiagnosticTrace.Write("wms", "loopback-create-command-started", ToolDetails(midiTool));
        var output = ProcessRunner.Run(
            midiTool,
            ["loopback", "create", "--name-a", AppPortName, "--name-b", PortName, "--unique-identifier", "WUBT10MidiBridge", "--association-id", AssociationId],
            TimeSpan.FromSeconds(15));
        DiagnosticTrace.Write("wms", "loopback-create-command-completed", new Dictionary<string, object?>
        {
            ["output"] = Truncate(output)
        });
        _created = true;

        Exception? lastError = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return WinMmMidiOutput.Open(AppPortName);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == 0 || attempt == 19)
                {
                    DiagnosticTrace.Write("wms", "loopback-output-open-retry", new Dictionary<string, object?>
                    {
                        ["attempt"] = attempt + 1,
                        ["exception"] = ex.Message
                    });
                }
                Thread.Sleep(250);
            }
        }

        throw new InvalidOperationException("The virtual MIDI port was created but did not become available.", lastError);
    }

    private static string FindMidiTool()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var expected = Path.Combine(programFiles, "Windows MIDI Services", "Tools", "Console", "midi.exe");
        if (File.Exists(expected))
            return expected;
        throw new FileNotFoundException("Windows MIDI Services Runtime and Tools are required for the integrated MIDI bridge.", expected);
    }

    public void Dispose()
    {
        if (!_created)
        {
            DiagnosticTrace.Write("wms", "loopback-remove-skipped", new Dictionary<string, object?> { ["reason"] = "not-created-or-reused" });
            return;
        }

        try
        {
            var midiTool = FindMidiTool();
            DiagnosticTrace.Write("wms", "loopback-remove-command-started", ToolDetails(midiTool));
            var output = ProcessRunner.Run(
                midiTool,
                ["loopback", "remove", "--association-id", AssociationId],
                TimeSpan.FromSeconds(10));
            DiagnosticTrace.Write("wms", "loopback-remove-command-completed", new Dictionary<string, object?>
            {
                ["output"] = Truncate(output)
            });
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("wms", "loopback-remove-command-failed", new Dictionary<string, object?>
            {
                ["exception"] = ex.ToString()
            });
            RepairLog.Write("Could not remove temporary MIDI loopback: " + ex);
        }
    }

    private static Dictionary<string, object?> ToolDetails(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        return new Dictionary<string, object?>
        {
            ["path"] = path,
            ["fileVersion"] = version.FileVersion,
            ["productVersion"] = version.ProductVersion,
            ["associationId"] = AssociationId
        };
    }

    private static string Truncate(string value) =>
        value.Length <= 4096 ? value : value[..4096] + "…";
}
