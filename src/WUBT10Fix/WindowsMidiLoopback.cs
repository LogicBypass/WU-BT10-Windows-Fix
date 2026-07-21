namespace WUBT10Fix;

internal sealed class WindowsMidiLoopback : IDisposable
{
    internal const string PortName = "WU-BT10 MIDI Bridge";
    private const string AppPortName = "WU-BT10 MIDI Bridge (App)";
    private const string AssociationId = "{F310E87A-6AFE-4B95-A108-546038EB8449}";
    private bool _created;

    internal WinMmMidiOutput OpenOutput()
    {
        try
        {
            var existing = WinMmMidiOutput.Open(AppPortName);
            _created = true;
            return existing;
        }
        catch (InvalidOperationException)
        {
            // The temporary endpoint does not exist yet.
        }

        var midiTool = FindMidiTool();
        ProcessRunner.Run(
            midiTool,
            ["loopback", "create", "--name-a", AppPortName, "--name-b", PortName, "--unique-identifier", "WUBT10MidiBridge", "--association-id", AssociationId],
            TimeSpan.FromSeconds(15));
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
            return;

        try
        {
            ProcessRunner.Run(
                FindMidiTool(),
                ["loopback", "remove", "--association-id", AssociationId],
                TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not remove temporary MIDI loopback: " + ex);
        }
    }
}
