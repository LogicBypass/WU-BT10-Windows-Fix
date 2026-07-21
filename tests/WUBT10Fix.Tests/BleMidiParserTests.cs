namespace WUBT10Fix.Tests;

public sealed class BleMidiParserTests
{
    [Fact]
    public void ParsesNoteOnAndNoteOffFromRealWuBt10Packet()
    {
        byte[] packet = [0xA1, 0xDC, 0x90, 0x39, 0x38, 0x9C, 0x80, 0x39, 0x40];

        var messages = BleMidiParser.ParseShortMessages(packet).ToArray();

        Assert.Equal([0x00383990u, 0x00403980u], messages);
    }

    [Fact]
    public void ParsesSingleNoteOnPacket()
    {
        byte[] packet = [0x80, 0x80, 0x90, 0x3C, 0x64];

        var messages = BleMidiParser.ParseShortMessages(packet).ToArray();

        Assert.Equal([0x00643C90u], messages);
    }
}
