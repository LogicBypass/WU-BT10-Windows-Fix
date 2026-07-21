using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal sealed class WinMmMidiOutput : IDisposable
{
    private IntPtr _handle;

    internal static WinMmMidiOutput Open(string nameFragment)
    {
        var deviceCount = midiOutGetNumDevs();
        DiagnosticTrace.Write("winmm", "output-search-started", new Dictionary<string, object?>
        {
            ["nameFragment"] = nameFragment,
            ["deviceCount"] = deviceCount
        });
        for (uint id = 0; id < deviceCount; id++)
        {
            var result = midiOutGetDevCaps(id, out var caps, (uint)Marshal.SizeOf<MidiOutCaps>());
            if (result != 0 || !caps.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
                continue;

            DiagnosticTrace.Write("winmm", "output-match-found", new Dictionary<string, object?>
            {
                ["deviceId"] = id,
                ["name"] = caps.Name,
                ["manufacturerId"] = caps.ManufacturerId,
                ["productId"] = caps.ProductId,
                ["driverVersion"] = caps.DriverVersion
            });
            result = midiOutOpen(out var handle, id, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != 0)
            {
                DiagnosticTrace.Write("winmm", "output-open-failed", new Dictionary<string, object?>
                {
                    ["deviceId"] = id,
                    ["name"] = caps.Name,
                    ["result"] = result
                });
                throw new Win32Exception((int)result, $"Could not open MIDI output '{caps.Name}'.");
            }
            DiagnosticTrace.Write("winmm", "output-opened", new Dictionary<string, object?>
            {
                ["deviceId"] = id,
                ["name"] = caps.Name
            });
            return new WinMmMidiOutput { _handle = handle };
        }

        DiagnosticTrace.Write("winmm", "output-not-found", new Dictionary<string, object?>
        {
            ["nameFragment"] = nameFragment,
            ["deviceCount"] = deviceCount
        });
        throw new InvalidOperationException($"MIDI output containing '{nameFragment}' was not found.");
    }

    internal void Send(uint packedMessage)
    {
        var handle = _handle;
        if (handle == IntPtr.Zero)
            return;
        var result = midiOutShortMsg(handle, packedMessage);
        if (result != 0)
        {
            DiagnosticTrace.Write("winmm", "short-message-send-failed", new Dictionary<string, object?> { ["result"] = result });
            throw new Win32Exception((int)result, "Windows rejected a MIDI message.");
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            var reset = midiOutReset(handle);
            var close = midiOutClose(handle);
            DiagnosticTrace.Write("winmm", "output-closed", new Dictionary<string, object?>
            {
                ["resetResult"] = reset,
                ["closeResult"] = close
            });
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        internal ushort ManufacturerId;
        internal ushort ProductId;
        internal uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        internal ushort Technology;
        internal ushort Voices;
        internal ushort Notes;
        internal ushort ChannelMask;
        internal uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiOutGetDevCaps(uint deviceId, out MidiOutCaps caps, uint capsSize);

    [DllImport("winmm.dll")]
    private static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    private static extern uint midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(IntPtr handle);
}
