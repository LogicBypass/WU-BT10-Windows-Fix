using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal sealed class WinMmMidiInput : IDisposable
{
    private const uint CallbackFunction = 0x00030000;
    private const uint MidiInputData = 0x03C3;
    private const uint MidiInputLongData = 0x03C4;
    private const uint MidiErrorStillPlaying = 65;
    private const int LongBufferCount = 4;
    private const int LongBufferSize = 64 * 1024;
    private const int MaximumSystemExclusiveBytes = 1024 * 1024;

    private readonly Action<byte[]> _messageReceived;
    private readonly MidiInputCallback _callback;
    private readonly List<LongBuffer> _longBuffers = [];
    private readonly List<byte> _systemExclusive = new(256);
    private readonly object _systemExclusiveGate = new();
    private IntPtr _handle;
    private int _stopping;
    private int _messageCount;

    private WinMmMidiInput(Action<byte[]> messageReceived)
    {
        _messageReceived = messageReceived;
        _callback = OnMidiInput;
    }

    internal static WinMmMidiInput Open(string nameFragment, Action<byte[]> messageReceived)
    {
        ArgumentNullException.ThrowIfNull(messageReceived);
        var deviceCount = midiInGetNumDevs();
        DiagnosticTrace.Write("winmm", "input-search-started", new Dictionary<string, object?>
        {
            ["nameFragment"] = nameFragment,
            ["deviceCount"] = deviceCount
        });

        for (uint id = 0; id < deviceCount; id++)
        {
            var result = midiInGetDevCaps(id, out var caps, checked((uint)Marshal.SizeOf<MidiInCaps>()));
            if (result != 0 || !caps.Name.Equals(nameFragment, StringComparison.OrdinalIgnoreCase))
                continue;

            var input = new WinMmMidiInput(messageReceived);
            try
            {
                result = midiInOpen(out input._handle, id, input._callback, IntPtr.Zero, CallbackFunction);
                if (result != 0)
                    throw new Win32Exception(checked((int)result), $"Could not open MIDI input '{caps.Name}'.");

                input.PrepareLongBuffers();
                result = midiInStart(input._handle);
                if (result != 0)
                    throw new Win32Exception(checked((int)result), $"Could not start MIDI input '{caps.Name}'.");

                DiagnosticTrace.Write("winmm", "input-opened", new Dictionary<string, object?>
                {
                    ["deviceId"] = id,
                    ["name"] = caps.Name,
                    ["longBufferCount"] = LongBufferCount,
                    ["longBufferBytes"] = LongBufferSize
                });
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException($"MIDI input named '{nameFragment}' was not found.");
    }

    private void PrepareLongBuffers()
    {
        var headerSize = checked((uint)Marshal.SizeOf<MidiHeader>());
        for (var index = 0; index < LongBufferCount; index++)
        {
            var buffer = new LongBuffer(LongBufferSize);
            _longBuffers.Add(buffer);
            var result = midiInPrepareHeader(_handle, buffer.Header, headerSize);
            if (result != 0)
                throw new Win32Exception(checked((int)result), "Windows could not prepare a MIDI SysEx input buffer.");
            buffer.Prepared = true;

            result = midiInAddBuffer(_handle, buffer.Header, headerSize);
            if (result != 0)
                throw new Win32Exception(checked((int)result), "Windows could not queue a MIDI SysEx input buffer.");
        }
    }

    private void OnMidiInput(IntPtr handle, uint message, IntPtr instance, IntPtr parameter1, IntPtr parameter2)
    {
        _ = handle;
        _ = instance;
        _ = parameter2;
        if (Volatile.Read(ref _stopping) != 0)
            return;

        try
        {
            switch (message)
            {
                case MidiInputData:
                    ProcessShortMessage(unchecked((uint)parameter1.ToInt64()));
                    break;
                case MidiInputLongData:
                    ProcessLongMessage(parameter1);
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticTrace.Write("winmm", "input-callback-failed", new Dictionary<string, object?>
            {
                ["callbackMessage"] = message,
                ["exception"] = ex.ToString()
            });
        }
    }

    private void ProcessShortMessage(uint packed)
    {
        var status = (byte)packed;
        var dataLength = BleMidiParser.MidiDataLength(status);
        if (dataLength < 0)
            return;

        var message = new byte[dataLength + 1];
        message[0] = status;
        if (dataLength > 0)
            message[1] = (byte)(packed >> 8);
        if (dataLength > 1)
            message[2] = (byte)(packed >> 16);
        Emit(message);
    }

    private void ProcessLongMessage(IntPtr headerPointer)
    {
        if (headerPointer == IntPtr.Zero)
            return;

        var header = Marshal.PtrToStructure<MidiHeader>(headerPointer);
        var count = checked((int)Math.Min(header.BytesRecorded, header.BufferLength));
        var data = new byte[count];
        if (count > 0)
            Marshal.Copy(header.Data, data, 0, count);

        if (Volatile.Read(ref _stopping) == 0)
        {
            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, headerPointer, false);
            var result = midiInAddBuffer(_handle, headerPointer, checked((uint)Marshal.SizeOf<MidiHeader>()));
            if (result != 0)
            {
                DiagnosticTrace.Write("winmm", "input-buffer-requeue-failed", new Dictionary<string, object?>
                {
                    ["result"] = result
                });
            }
        }

        if (data.Length == 0)
            return;

        List<byte[]> completed = [];
        lock (_systemExclusiveGate)
        {
            foreach (var value in data)
            {
                if (value >= 0xF8)
                {
                    completed.Add([value]);
                    continue;
                }
                if (value == 0xF0)
                {
                    _systemExclusive.Clear();
                    _systemExclusive.Add(value);
                    continue;
                }
                if (_systemExclusive.Count == 0)
                    continue;
                if (_systemExclusive.Count >= MaximumSystemExclusiveBytes)
                {
                    _systemExclusive.Clear();
                    continue;
                }

                _systemExclusive.Add(value);
                if (value == 0xF7)
                {
                    completed.Add(_systemExclusive.ToArray());
                    _systemExclusive.Clear();
                }
            }
        }

        foreach (var message in completed)
            Emit(message);
    }

    private void Emit(byte[] message)
    {
        var count = Interlocked.Increment(ref _messageCount);
        if (count == 1 || count % 100 == 0)
        {
            DiagnosticTrace.Write("winmm", "input-message-checkpoint", new Dictionary<string, object?>
            {
                ["messageCount"] = count,
                ["byteCount"] = message.Length,
                ["isSystemExclusive"] = message[0] == 0xF0
            });
        }
        _messageReceived(message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
            return;

        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            var stop = midiInStop(handle);
            var reset = midiInReset(handle);
            var headerSize = checked((uint)Marshal.SizeOf<MidiHeader>());
            foreach (var buffer in _longBuffers)
            {
                if (!buffer.Prepared)
                    continue;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    var result = midiInUnprepareHeader(handle, buffer.Header, headerSize);
                    if (result == 0)
                    {
                        buffer.Prepared = false;
                        break;
                    }
                    if (result != MidiErrorStillPlaying)
                        break;
                    Thread.Sleep(10);
                }
                if (buffer.Prepared)
                {
                    DiagnosticTrace.Write("winmm", "input-buffer-release-deferred", new Dictionary<string, object?>
                    {
                        ["reason"] = "driver-still-owns-buffer"
                    });
                }
            }
            var close = midiInClose(handle);
            DiagnosticTrace.Write("winmm", "input-closed", new Dictionary<string, object?>
            {
                ["stopResult"] = stop,
                ["resetResult"] = reset,
                ["closeResult"] = close,
                ["messageCount"] = Volatile.Read(ref _messageCount)
            });
        }

        foreach (var buffer in _longBuffers)
        {
            if (!buffer.Prepared)
                buffer.Dispose();
        }
        _longBuffers.Clear();
        lock (_systemExclusiveGate)
            _systemExclusive.Clear();
    }

    private sealed class LongBuffer : IDisposable
    {
        internal LongBuffer(int size)
        {
            Data = Marshal.AllocHGlobal(size);
            var header = new MidiHeader
            {
                Data = Data,
                BufferLength = checked((uint)size),
                Reserved = new IntPtr[8]
            };
            Header = Marshal.AllocHGlobal(Marshal.SizeOf<MidiHeader>());
            Marshal.StructureToPtr(header, Header, false);
        }

        internal IntPtr Data { get; }
        internal IntPtr Header { get; }
        internal bool Prepared { get; set; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Header);
            Marshal.FreeHGlobal(Data);
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
    private struct MidiInCaps
    {
        internal ushort ManufacturerId;
        internal ushort ProductId;
        internal uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        internal uint Support;
    }

    private delegate void MidiInputCallback(
        IntPtr handle,
        uint message,
        IntPtr instance,
        IntPtr parameter1,
        IntPtr parameter2);

    [DllImport("winmm.dll")]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetDevCaps(uint deviceId, out MidiInCaps caps, uint capsSize);

    [DllImport("winmm.dll")]
    private static extern uint midiInOpen(
        out IntPtr handle,
        uint deviceId,
        MidiInputCallback callback,
        IntPtr instance,
        uint flags);

    [DllImport("winmm.dll")]
    private static extern uint midiInPrepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiInUnprepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiInAddBuffer(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint midiInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInClose(IntPtr handle);
}
