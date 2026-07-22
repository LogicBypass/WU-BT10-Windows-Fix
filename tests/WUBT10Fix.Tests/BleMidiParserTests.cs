namespace WUBT10Fix.Tests;

public sealed class BleMidiParserTests
{
    [Fact]
    public void ParsesNoteOnAndNoteOffFromRealWuBt10Packet()
    {
        byte[] packet = [0xA1, 0xDC, 0x90, 0x39, 0x38, 0x9C, 0x80, 0x39, 0x40];
        var parser = new BleMidiParser();

        var messages = parser.Parse(packet);

        Assert.Equal(2, messages.Count);
        Assert.Equal([0x90, 0x39, 0x38], messages[0]);
        Assert.Equal([0x80, 0x39, 0x40], messages[1]);
    }

    [Fact]
    public void PreservesRunningStatusAcrossPackets()
    {
        var parser = new BleMidiParser();

        var first = parser.Parse([0x80, 0x80, 0x90, 0x3C, 0x64]);
        var second = parser.Parse([0x80, 0x81, 0x3D, 0x65]);

        Assert.Equal([0x90, 0x3C, 0x64], Assert.Single(first));
        Assert.Equal([0x90, 0x3D, 0x65], Assert.Single(second));
    }

    [Fact]
    public void PreservesRunningStatusAroundRealTimeMessage()
    {
        var parser = new BleMidiParser();

        var messages = parser.Parse([0x80, 0x80, 0x90, 0x3C, 0x64, 0x81, 0xF8, 0x82, 0x3D, 0x65]);

        Assert.Collection(
            messages,
            message => Assert.Equal([0x90, 0x3C, 0x64], message),
            message => Assert.Equal([0xF8], message),
            message => Assert.Equal([0x90, 0x3D, 0x65], message));
    }

    [Fact]
    public void ReassemblesSystemExclusiveAcrossPacketsWithInterleavedRealTime()
    {
        var parser = new BleMidiParser();

        var first = parser.Parse([0x80, 0x80, 0xF0, 0x44, 0x01]);
        var second = parser.Parse([0x80, 0x02, 0x81, 0xF8, 0x82, 0xF7]);

        Assert.Empty(first);
        Assert.Collection(
            second,
            message => Assert.Equal([0xF8], message),
            message => Assert.Equal([0xF0, 0x44, 0x01, 0x02, 0xF7], message));
    }

    [Fact]
    public void ResetClearsRunningStatusAndPartialSystemExclusive()
    {
        var parser = new BleMidiParser();
        _ = parser.Parse([0x80, 0x80, 0x90, 0x3C, 0x64]);
        _ = parser.Parse([0x80, 0x81, 0xF0, 0x01]);

        parser.Reset();

        Assert.Empty(parser.Parse([0x80, 0x82, 0x3D, 0x65]));
        Assert.Empty(parser.Parse([0x80, 0x83, 0xF7]));
    }

    [Fact]
    public void EncodeSingleAddsBleTimestampFraming()
    {
        var packet = BleMidiParser.EncodeSingle([0x90, 0x3C, 0x64]);

        Assert.Equal(5, packet.Length);
        Assert.Equal(0x80, packet[0] & 0xC0);
        Assert.Equal(0x80, packet[1] & 0x80);
        Assert.Equal([0x90, 0x3C, 0x64], packet[2..]);
    }

    [Fact]
    public void EncodeSystemExclusiveHonorsPacketSizeAndFinalTimestamp()
    {
        byte[] message = [0xF0, 1, 2, 3, 4, 5, 6, 7, 8, 0xF7];

        var packets = BleMidiParser.EncodeSystemExclusive(message, 6);

        Assert.All(packets, packet => Assert.InRange(packet.Length, 3, 6));
        Assert.Equal(0xF0, packets[0][2]);
        Assert.Equal(0xF7, packets[^1][2]);
        Assert.Equal(3, packets[^1].Length);
    }
}
