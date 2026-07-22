namespace WUBT10Fix.Tests;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void TryAcquireBlocksSecondOwnerAndAllowsOwnerAfterRelease()
    {
        var name = @"Local\WU-BT10-Windows-Fix-Test-" + Guid.NewGuid().ToString("N");

        using (var first = SingleInstanceLease.TryAcquire(name))
        {
            Assert.True(first.IsAcquired);

            using var second = SingleInstanceLease.TryAcquire(name);
            Assert.False(second.IsAcquired);
        }

        using var afterRelease = SingleInstanceLease.TryAcquire(name);
        Assert.True(afterRelease.IsAcquired);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var lease = SingleInstanceLease.TryAcquire(
            @"Local\WU-BT10-Windows-Fix-Test-" + Guid.NewGuid().ToString("N"));

        lease.Dispose();
        lease.Dispose();
    }
}
