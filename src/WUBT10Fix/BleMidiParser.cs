namespace WUBT10Fix;

internal sealed class BleMidiParser
{
    private const int MaximumSystemExclusiveBytes = 1024 * 1024;

    private readonly List<byte> _systemExclusive = new(256);
    private readonly List<byte> _pendingData = new(2);
    private byte _runningStatus;
    private byte _pendingStatus;
    private int _pendingDataLength;
    private bool _inSystemExclusive;

    internal void Reset()
    {
        _systemExclusive.Clear();
        _pendingData.Clear();
        _runningStatus = 0;
        _pendingStatus = 0;
        _pendingDataLength = 0;
        _inSystemExclusive = false;
    }

    internal IReadOnlyList<byte[]> Parse(ReadOnlySpan<byte> packet)
    {
        var messages = new List<byte[]>();
        if (packet.Length == 0 || (packet[0] & 0x80) == 0 || (packet[0] & 0x40) != 0)
            return messages;

        var index = 1;
        if (_inSystemExclusive && index < packet.Length && (packet[index] & 0x80) == 0)
            AppendSystemExclusiveData(packet, ref index);

        while (index < packet.Length)
        {
            if ((packet[index] & 0x80) == 0)
            {
                index++;
                continue;
            }

            // Timestamp-low byte. The bridge forwards immediately, so only
            // its framing role matters here.
            index++;
            if (index >= packet.Length)
                break;

            if (_inSystemExclusive)
            {
                if ((packet[index] & 0x80) == 0)
                {
                    AppendSystemExclusiveData(packet, ref index);
                    continue;
                }

                var systemExclusiveStatus = packet[index++];
                if (systemExclusiveStatus == 0xF7)
                {
                    CompleteSystemExclusive(messages);
                    continue;
                }
                if (systemExclusiveStatus >= 0xF8)
                {
                    messages.Add([systemExclusiveStatus]);
                    continue;
                }

                // A non-real-time status aborts malformed, unterminated SysEx
                // and begins a fresh MIDI message.
                ClearSystemExclusive();
                ProcessStatus(systemExclusiveStatus, packet, ref index, messages);
                continue;
            }

            if ((packet[index] & 0x80) != 0)
            {
                var status = packet[index++];
                ProcessStatus(status, packet, ref index, messages);
            }
            else
            {
                ContinueRunningOrPendingMessage(packet, ref index, messages);
            }
        }

        return messages;
    }

    private void ProcessStatus(
        byte status,
        ReadOnlySpan<byte> packet,
        ref int index,
        List<byte[]> messages)
    {
        if (status >= 0xF8)
        {
            messages.Add([status]);
            return;
        }

        if (status == 0xF0)
        {
            _pendingData.Clear();
            _pendingStatus = 0;
            _pendingDataLength = 0;
            _runningStatus = 0;
            _systemExclusive.Clear();
            _systemExclusive.Add(0xF0);
            _inSystemExclusive = true;
            AppendSystemExclusiveData(packet, ref index);
            return;
        }

        if (status == 0xF7)
        {
            _runningStatus = 0;
            ClearPendingMessage();
            return;
        }

        var dataLength = MidiDataLength(status);
        if (dataLength < 0)
        {
            if (status >= 0xF0)
                _runningStatus = 0;
            ClearPendingMessage();
            return;
        }

        ClearPendingMessage();
        _pendingStatus = status;
        _pendingDataLength = dataLength;
        _runningStatus = status < 0xF0 ? status : (byte)0;
        ConsumePendingData(packet, ref index, messages);
    }

    private void ContinueRunningOrPendingMessage(
        ReadOnlySpan<byte> packet,
        ref int index,
        List<byte[]> messages)
    {
        if (_pendingStatus == 0)
        {
            if (_runningStatus == 0)
            {
                index++;
                return;
            }

            _pendingStatus = _runningStatus;
            _pendingDataLength = MidiDataLength(_runningStatus);
            _pendingData.Clear();
        }

        ConsumePendingData(packet, ref index, messages);
    }

    private void ConsumePendingData(
        ReadOnlySpan<byte> packet,
        ref int index,
        List<byte[]> messages)
    {
        while (_pendingData.Count < _pendingDataLength && index < packet.Length)
        {
            if ((packet[index] & 0x80) != 0)
                return;
            _pendingData.Add(packet[index++]);
        }

        if (_pendingData.Count != _pendingDataLength)
            return;

        var message = new byte[_pendingDataLength + 1];
        message[0] = _pendingStatus;
        _pendingData.CopyTo(message, 1);
        messages.Add(message);
        ClearPendingMessage();
    }

    private void AppendSystemExclusiveData(ReadOnlySpan<byte> packet, ref int index)
    {
        while (index < packet.Length && (packet[index] & 0x80) == 0)
        {
            if (_systemExclusive.Count >= MaximumSystemExclusiveBytes)
            {
                ClearSystemExclusive();
                return;
            }
            _systemExclusive.Add(packet[index++]);
        }
    }

    private void CompleteSystemExclusive(List<byte[]> messages)
    {
        if (!_inSystemExclusive)
            return;
        _systemExclusive.Add(0xF7);
        messages.Add(_systemExclusive.ToArray());
        ClearSystemExclusive();
        _runningStatus = 0;
    }

    private void ClearSystemExclusive()
    {
        _systemExclusive.Clear();
        _inSystemExclusive = false;
    }

    private void ClearPendingMessage()
    {
        _pendingData.Clear();
        _pendingStatus = 0;
        _pendingDataLength = 0;
    }

    internal static int MidiDataLength(byte status)
    {
        if (status is >= 0x80 and <= 0xBF || status is >= 0xE0 and <= 0xEF)
            return 2;
        if (status is >= 0xC0 and <= 0xDF || status is 0xF1 or 0xF3)
            return 1;
        if (status == 0xF2)
            return 2;
        if (status is 0xF6 or >= 0xF8)
            return 0;
        return -1;
    }

    internal static byte[] EncodeSingle(ReadOnlySpan<byte> midi)
    {
        if (midi.Length == 0)
            return [];

        var timestamp = (int)(Environment.TickCount64 & 0x1FFF);
        var packet = new byte[midi.Length + 2];
        packet[0] = (byte)(0x80 | ((timestamp >> 7) & 0x3F));
        packet[1] = (byte)(0x80 | (timestamp & 0x7F));
        midi.CopyTo(packet.AsSpan(2));
        return packet;
    }

    internal static IReadOnlyList<byte[]> EncodeSystemExclusive(
        ReadOnlySpan<byte> systemExclusive,
        int maximumPacketSize)
    {
        if (systemExclusive.Length < 2 || systemExclusive[0] != 0xF0 || systemExclusive[^1] != 0xF7)
            throw new ArgumentException("A MIDI SysEx message must start with F0 and end with F7.", nameof(systemExclusive));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPacketSize, 3);

        var timestamp = (int)(Environment.TickCount64 & 0x1FFF);
        var header = (byte)(0x80 | ((timestamp >> 7) & 0x3F));
        var timestampLow = (byte)(0x80 | (timestamp & 0x7F));
        var packets = new List<byte[]>();
        var index = 0;

        var firstCount = Math.Min(systemExclusive.Length - 1, maximumPacketSize - 2);
        var first = new byte[firstCount + 2];
        first[0] = header;
        first[1] = timestampLow;
        systemExclusive[..firstCount].CopyTo(first.AsSpan(2));
        packets.Add(first);
        index += firstCount;

        while (systemExclusive.Length - index > 1)
        {
            var count = Math.Min(systemExclusive.Length - index - 1, maximumPacketSize - 1);
            var continuation = new byte[count + 1];
            continuation[0] = header;
            systemExclusive.Slice(index, count).CopyTo(continuation.AsSpan(1));
            packets.Add(continuation);
            index += count;
        }

        packets.Add([header, timestampLow, 0xF7]);
        return packets;
    }
}
