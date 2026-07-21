namespace WUBT10Fix;

internal static class BleMidiParser
{
    internal static IReadOnlyList<uint> ParseShortMessages(ReadOnlySpan<byte> packet)
    {
        var messages = new List<uint>();
        if (packet.Length < 3 || (packet[0] & 0x80) == 0 || (packet[1] & 0x80) == 0)
            return messages;

        var index = 2;
        var status = packet[index++];
        while (index <= packet.Length)
        {
            var dataLength = MidiDataLength(status);
            if (dataLength < 0 || index + dataLength > packet.Length)
                break;

            uint packed = status;
            if (dataLength > 0)
                packed |= (uint)packet[index++] << 8;
            if (dataLength > 1)
                packed |= (uint)packet[index++] << 16;
            messages.Add(packed);

            if (index >= packet.Length)
                break;

            // A high-bit byte between messages is the BLE timestamp-low byte.
            // If another high-bit byte follows it, that byte is a new MIDI
            // status; otherwise MIDI running status continues immediately.
            if ((packet[index] & 0x80) != 0)
            {
                index++;
                if (index < packet.Length && (packet[index] & 0x80) != 0)
                    status = packet[index++];
            }
        }

        return messages;
    }

    internal static int MidiDataLength(byte status)
    {
        if (status is >= 0x80 and <= 0xBF || status is >= 0xE0 and <= 0xEF)
            return 2;
        if (status is >= 0xC0 and <= 0xDF || status is 0xF1 or 0xF3)
            return 1;
        if (status is 0xF2)
            return 2;
        if (status is >= 0xF6)
            return 0;
        return -1; // SysEx is intentionally not required for live note input.
    }
}
