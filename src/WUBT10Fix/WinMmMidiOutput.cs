using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal sealed class WinMmMidiOutput : IDisposable
{
    private IntPtr _handle;

    internal static WinMmMidiOutput Open(string nameFragment)
    {
        for (uint id = 0; id < midiOutGetNumDevs(); id++)
        {
            var result = midiOutGetDevCaps(id, out var caps, (uint)Marshal.SizeOf<MidiOutCaps>());
            if (result != 0 || !caps.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
                continue;

            result = midiOutOpen(out var handle, id, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != 0)
                throw new Win32Exception((int)result, $"Could not open MIDI output '{caps.Name}'.");
            return new WinMmMidiOutput { _handle = handle };
        }

        throw new InvalidOperationException($"MIDI output containing '{nameFragment}' was not found.");
    }

    internal void Send(uint packedMessage)
    {
        var handle = _handle;
        if (handle == IntPtr.Zero)
            return;
        var result = midiOutShortMsg(handle, packedMessage);
        if (result != 0)
            throw new Win32Exception((int)result, "Windows rejected a MIDI message.");
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            _ = midiOutReset(handle);
            _ = midiOutClose(handle);
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
