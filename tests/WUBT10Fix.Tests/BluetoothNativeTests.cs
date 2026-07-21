namespace WUBT10Fix.Tests;

public sealed class BluetoothNativeTests
{
    [Theory]
    [InlineData(0x0000020000000001ul, "02:00:00:00:00:01")]
    [InlineData(0x0000000000000000ul, "00:00:00:00:00:00")]
    [InlineData(0x0000AABBCCDDEEFFul, "AA:BB:CC:DD:EE:FF")]
    public void FormatAddressUsesCanonicalBluetoothNotation(ulong address, string expected)
    {
        Assert.Equal(expected, BluetoothNative.FormatAddress(address));
    }
}
