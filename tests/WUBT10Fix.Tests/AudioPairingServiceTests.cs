using Windows.Devices.Enumeration;

namespace WUBT10Fix.Tests;

public sealed class AudioPairingServiceTests
{
    [Fact]
    public void ExactNameMatchesRejectsLookalikeDevices()
    {
        string[] candidates =
        [
            "WU-BT10 AUDIO",
            " WU-BT10 AUDIO ",
            "wu-bt10 audio",
            "WU-BT10 AUDIO 2",
            "MY WU-BT10 AUDIO",
            "WU-BT10 MIDI"
        ];

        var matches = AudioPairingService.ExactNameMatches(candidates, value => value);

        Assert.Equal(3, matches.Count);
        Assert.DoesNotContain("WU-BT10 AUDIO 2", matches);
        Assert.DoesNotContain("MY WU-BT10 AUDIO", matches);
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.Paired)]
    [InlineData(DevicePairingResultStatus.AlreadyPaired)]
    public void SuccessfulPairingStatusesAreAccepted(DevicePairingResultStatus status)
    {
        Assert.True(AudioPairingService.IsSuccessfulStatus(status));
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.AuthenticationFailure)]
    [InlineData(DevicePairingResultStatus.AuthenticationTimeout)]
    [InlineData(DevicePairingResultStatus.ConnectionRejected)]
    [InlineData(DevicePairingResultStatus.Failed)]
    [InlineData(DevicePairingResultStatus.NotReadyToPair)]
    [InlineData(DevicePairingResultStatus.OperationAlreadyInProgress)]
    [InlineData(DevicePairingResultStatus.TooManyConnections)]
    public void FailurePairingStatusesAreRejected(DevicePairingResultStatus status)
    {
        Assert.False(AudioPairingService.IsSuccessfulStatus(status));
        Assert.False(string.IsNullOrWhiteSpace(AudioPairingService.PairingFailureMessage(status)));
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.Failed, 0, true)]
    [InlineData(DevicePairingResultStatus.Failed, 1999, true)]
    [InlineData(DevicePairingResultStatus.Failed, 2000, false)]
    [InlineData(DevicePairingResultStatus.Failed, 70000, false)]
    [InlineData(DevicePairingResultStatus.AuthenticationFailure, 10, false)]
    [InlineData(DevicePairingResultStatus.PairingCanceled, 10, false)]
    public void OnlyImmediateGenericFailureIsRetried(
        DevicePairingResultStatus status,
        int elapsedMilliseconds,
        bool expected)
    {
        Assert.Equal(
            expected,
            AudioPairingService.ShouldRetryImmediateFailure(
                status,
                TimeSpan.FromMilliseconds(elapsedMilliseconds)));
    }
}
