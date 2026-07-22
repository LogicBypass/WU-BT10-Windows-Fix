namespace WUBT10Fix.Tests;

public sealed class AudioPlaybackTesterTests
{
    [Fact]
    public void TestWaveIsStereoPcmAt48KhzWithCorrectLength()
    {
        var wave = AudioPlaybackTester.CreateTestWave(TimeSpan.FromSeconds(1));

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(2, BitConverter.ToInt16(wave, 22));
        Assert.Equal(48000, BitConverter.ToInt32(wave, 24));
        Assert.Equal(16, BitConverter.ToInt16(wave, 34));
        Assert.Equal(44 + 48000 * 2 * 2, wave.Length);
    }

    [Fact]
    public void TestWaveRejectsOverflowingDuration()
    {
        Assert.ThrowsAny<OverflowException>(() =>
            AudioPlaybackTester.CreateTestWave(TimeSpan.FromDays(365)));
    }
}
