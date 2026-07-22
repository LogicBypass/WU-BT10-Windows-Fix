namespace WUBT10Fix.Tests;

public sealed class AudioPlaybackTesterTests
{
    [Fact]
    public async Task SilentActivationRejectsNonAudioEndpointBeforeBluetoothControl()
    {
        var result = await AudioPlaybackTester.ActivateSilentlyPreservedEndpointAsync(
            "BTHENUM\\not-an-audio-endpoint");

        Assert.False(result.Success);
        Assert.Contains("not a Windows audio endpoint", result.Message);
    }
}
