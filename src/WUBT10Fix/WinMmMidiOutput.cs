using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal sealed class WinMmMidiOutput : IDisposable
{
    private const uint MidiHeaderDone = 0x00000001;
    private const uint MidiErrorStillPlaying = 65;

    private readonly object _sendGate = new();
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
            if (result != 0 || !caps.Name.Equals(nameFragment, StringComparison.OrdinalIgnoreCase))
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
        throw new InvalidOperationException($"MIDI output named '{nameFragment}' was not found.");
    }

    internal void Send(ReadOnlySpan<byte> message)
    {
        if (message.Length == 0 || (message[0] & 0x80) == 0)
            return;

        lock (_sendGate)
        {
            var handle = _handle;
            if (handle == IntPtr.Zero)
                return;

            if (message[0] == 0xF0)
            {
                SendLong(handle, message);
                return;
            }

            uint packedMessage = message[0];
            if (message.Length > 1)
                packedMessage |= (uint)message[1] << 8;
            if (message.Length > 2)
                packedMessage |= (uint)message[2] << 16;
            var result = midiOutShortMsg(handle, packedMessage);
            if (result != 0)
            {
                DiagnosticTrace.Write("winmm", "short-message-send-failed", new Dictionary<string, object?> { ["result"] = result });
                throw new Win32Exception((int)result, "Windows rejected a MIDI message.");
            }
        }
    }

    private static void SendLong(IntPtr handle, ReadOnlySpan<byte> message)
    {
        var data = Marshal.AllocHGlobal(message.Length);
        var headerPointer = IntPtr.Zero;
        var prepared = false;
        try
        {
            Marshal.Copy(message.ToArray(), 0, data, message.Length);
            var header = new MidiHeader
            {
                Data = data,
                BufferLength = checked((uint)message.Length),
                Reserved = new IntPtr[8]
            };
            var headerSize = checked((uint)Marshal.SizeOf<MidiHeader>());
            headerPointer = Marshal.AllocHGlobal(checked((int)headerSize));
            Marshal.StructureToPtr(header, headerPointer, false);

            var result = midiOutPrepareHeader(handle, headerPointer, headerSize);
            if (result != 0)
                throw new Win32Exception((int)result, "Windows could not prepare a MIDI SysEx message.");
            prepared = true;

            result = midiOutLongMsg(handle, headerPointer, headerSize);
            if (result != 0)
                throw new Win32Exception((int)result, "Windows rejected a MIDI SysEx message.");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                header = Marshal.PtrToStructure<MidiHeader>(headerPointer);
                if ((header.Flags & MidiHeaderDone) != 0)
                    break;
                Thread.Sleep(1);
            }

            header = Marshal.PtrToStructure<MidiHeader>(headerPointer);
            if ((header.Flags & MidiHeaderDone) == 0)
            {
                _ = midiOutReset(handle);
                throw new TimeoutException("Windows did not finish forwarding a MIDI SysEx message within 15 seconds.");
            }

            DiagnosticTrace.Write("winmm", "long-message-sent", new Dictionary<string, object?>
            {
                ["byteCount"] = message.Length
            });
        }
        finally
        {
            var safeToFree = !prepared;
            if (prepared && headerPointer != IntPtr.Zero)
            {
                var headerSize = checked((uint)Marshal.SizeOf<MidiHeader>());
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    var result = midiOutUnprepareHeader(handle, headerPointer, headerSize);
                    if (result == 0)
                    {
                        safeToFree = true;
                        break;
                    }
                    if (result != MidiErrorStillPlaying)
                        break;
                    Thread.Sleep(10);
                }
            }
            if (safeToFree)
            {
                if (headerPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(headerPointer);
                Marshal.FreeHGlobal(data);
            }
            else
            {
                DiagnosticTrace.Write("winmm", "long-message-buffer-release-deferred", new Dictionary<string, object?>
                {
                    ["reason"] = "driver-still-owns-buffer"
                });
            }
        }
    }

    public void Dispose()
    {
        lock (_sendGate)
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                return;

            var reset = midiOutReset(handle);
            var close = midiOutClose(handle);
            DiagnosticTrace.Write("winmm", "output-closed", new Dictionary<string, object?>
            {
                ["resetResult"] = reset,
                ["closeResult"] = close
            });
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHeader
    {
        internal IntPtr Data;
        internal uint BufferLength;
        internal uint BytesRecorded;
        internal IntPtr UserData;
        internal uint Flags;
        internal IntPtr Next;
        internal IntPtr ReservedPointer;
        internal uint Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] internal IntPtr[] Reserved;
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
    private static extern uint midiOutPrepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiOutLongMsg(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(IntPtr handle);
}
