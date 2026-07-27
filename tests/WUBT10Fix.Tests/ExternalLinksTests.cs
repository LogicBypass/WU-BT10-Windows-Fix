using System.Diagnostics;

namespace WUBT10Fix.Tests;

public sealed class ExternalLinksTests
{
    [Theory]
    [InlineData(ExternalLinks.WindowsMidiServices)]
    [InlineData(ExternalLinks.SupportOnce)]
    [InlineData(ExternalLinks.SupportMonthly)]
    public void IsAllowedAcceptsOnlyDeclaredHttpsDestinations(string url)
    {
        Assert.True(ExternalLinks.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://ko-fi.com/wubt10fix")]
    [InlineData("https://ko-fi.com/wubt10fix/evil")]
    [InlineData("https://example.com/")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("")]
    public void IsAllowedRejectsUntrustedDestinations(string url)
    {
        Assert.False(ExternalLinks.IsAllowed(url));
    }

    [Fact]
    public void TryOpenUsesTheWindowsShellForAnAllowedUrl()
    {
        ProcessStartInfo? captured = null;

        var opened = ExternalLinks.TryOpen(
            ExternalLinks.SupportOnce,
            startInfo => captured = startInfo);

        Assert.True(opened);
        Assert.NotNull(captured);
        Assert.Equal(ExternalLinks.SupportOnce, captured.FileName);
        Assert.True(captured.UseShellExecute);
    }

    [Fact]
    public void TryOpenDoesNotLaunchAnUntrustedUrl()
    {
        var launchCalled = false;

        var opened = ExternalLinks.TryOpen(
            "https://example.com/",
            _ => launchCalled = true);

        Assert.False(opened);
        Assert.False(launchCalled);
    }
}
