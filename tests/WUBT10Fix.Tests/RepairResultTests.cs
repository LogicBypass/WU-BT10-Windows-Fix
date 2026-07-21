namespace WUBT10Fix.Tests;

public sealed class RepairResultTests
{
    [Fact]
    public void CompletedMarksEndpointPresent()
    {
        var result = RepairResult.Completed("done");

        Assert.True(result.Success);
        Assert.True(result.EndpointPresent);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public void FailedDoesNotMarkEndpointPresent()
    {
        var result = RepairResult.Failed("failed");

        Assert.False(result.Success);
        Assert.False(result.EndpointPresent);
    }

    [Fact]
    public void CompletedHardwareRestoreCanRequireManualReconnect()
    {
        var result = RepairResult.Completed("restored", endpointPresent: false);

        Assert.True(result.Success);
        Assert.False(result.EndpointPresent);
        Assert.Equal("restored", result.Message);
    }

}
