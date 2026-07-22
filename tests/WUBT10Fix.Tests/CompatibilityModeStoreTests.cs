namespace WUBT10Fix.Tests;

public sealed class CompatibilityModeStoreTests
{
    [Fact]
    public void MatchingIsCaseInsensitiveAndExact()
    {
        var state = new CompatibilityModeState("INTELAUDIO\\DEVICE", DateTimeOffset.UtcNow);

        Assert.True(CompatibilityModeStore.Matches(state, ["intelaudio\\device"]));
        Assert.False(CompatibilityModeStore.Matches(state, ["INTELAUDIO\\DEVICE-OTHER"]));
        Assert.False(CompatibilityModeStore.Matches(null, ["INTELAUDIO\\DEVICE"]));
    }
}
