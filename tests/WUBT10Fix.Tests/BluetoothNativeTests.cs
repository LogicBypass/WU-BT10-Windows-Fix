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

    [Theory]
    [InlineData("SetWuBt10AudioServiceState")]
    [InlineData("DisconnectWuBt10AudioTransport")]
    public void UnsafeBluetoothMutationMethodsAreNotExposed(string methodName)
    {
        var method = typeof(BluetoothNative).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        Assert.Null(method);
    }

    [Fact]
    public void UnsafeA2dpDeviceRestartMethodIsNotExposed()
    {
        var method = typeof(RepairEngine).GetMethod(
            "RestartWuBt10AudioDeviceNode",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        Assert.Null(method);
    }
}
