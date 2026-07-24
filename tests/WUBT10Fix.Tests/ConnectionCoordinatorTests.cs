namespace WUBT10Fix.Tests;

public sealed class ConnectionCoordinatorTests
{
    [Fact]
    public async Task ConnectAudioStopsMidiWaitsForAdvertisementThenConnects()
    {
        var events = new List<string>();
        var midiRunning = true;
        var audioConnected = false;
        using var coordinator = new ConnectionCoordinator(
            () => audioConnected,
            () => midiRunning,
            _ =>
            {
                events.Add("audio-connect");
                audioConnected = true;
                return Task.FromResult(new AudioSwitchResult(true, "Audio connected.", true));
            },
            _ => throw new InvalidOperationException("Audio disconnect was not expected."),
            _ => throw new InvalidOperationException("MIDI start was not expected."),
            () =>
            {
                events.Add("midi-stop");
                midiRunning = false;
                return Task.CompletedTask;
            },
            _ =>
            {
                events.Add("midi-advertisement");
                return Task.CompletedTask;
            });

        var result = await coordinator.ConnectAudioAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionMode.Audio, result.ActiveMode);
        Assert.Equal(["midi-stop", "midi-advertisement", "audio-connect"], events);
        Assert.True(audioConnected);
        Assert.False(midiRunning);
    }

    [Fact]
    public async Task ConnectMidiPreparesSessionBeforeReleasingAudio()
    {
        var events = new List<string>();
        var midiRunning = false;
        var audioConnected = true;
        ConnectionCoordinator? coordinator = null;
        coordinator = new ConnectionCoordinator(
            () => audioConnected,
            () => midiRunning,
            _ => throw new InvalidOperationException("Audio connect was not expected."),
            _ =>
            {
                events.Add("audio-disconnect");
                audioConnected = false;
                return Task.FromResult(new AudioSwitchResult(true, "Audio disconnected.", false));
            },
            async cancellationToken =>
            {
                events.Add("midi-session-prepared");
                await coordinator!.ReleaseAudioForMidiHandoffAsync(cancellationToken);
                events.Add("midi-connected");
                midiRunning = true;
            },
            () =>
            {
                events.Add("midi-stop");
                midiRunning = false;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        using (coordinator)
        {
            var result = await coordinator.ConnectMidiAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ConnectionMode.Midi, result.ActiveMode);
            Assert.Equal(["midi-session-prepared", "audio-disconnect", "midi-connected"], events);
            Assert.False(audioConnected);
            Assert.True(midiRunning);
        }
    }

    [Fact]
    public async Task ConnectMidiAbortsWhenAudioCannotBeReleased()
    {
        var midiRunning = false;
        var audioConnected = true;
        ConnectionCoordinator? coordinator = null;
        coordinator = new ConnectionCoordinator(
            () => audioConnected,
            () => midiRunning,
            _ => throw new InvalidOperationException("Audio connect was not expected."),
            _ => Task.FromResult(AudioSwitchResult.Failed("Driver refused Audio disconnect.")),
            async cancellationToken =>
            {
                await coordinator!.ReleaseAudioForMidiHandoffAsync(cancellationToken);
                midiRunning = true;
            },
            () =>
            {
                midiRunning = false;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        using (coordinator)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ConnectMidiAsync(TestContext.Current.CancellationToken));

            Assert.Contains("Driver refused", error.Message);
            Assert.True(audioConnected);
            Assert.False(midiRunning);
        }
    }

    [Fact]
    public async Task DisconnectMidiWaitsUntilAdapterAdvertises()
    {
        var events = new List<string>();
        var midiRunning = true;
        using var coordinator = new ConnectionCoordinator(
            () => false,
            () => midiRunning,
            _ => throw new InvalidOperationException("Audio connect was not expected."),
            _ => throw new InvalidOperationException("Audio disconnect was not expected."),
            _ => throw new InvalidOperationException("MIDI start was not expected."),
            () =>
            {
                events.Add("midi-stop");
                midiRunning = false;
                return Task.CompletedTask;
            },
            _ =>
            {
                events.Add("midi-advertisement");
                return Task.CompletedTask;
            });

        var result = await coordinator.DisconnectMidiAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionMode.None, result.ActiveMode);
        Assert.Equal(["midi-stop", "midi-advertisement"], events);
        Assert.False(midiRunning);
    }

    [Fact]
    public async Task OperationsAreSerialized()
    {
        var entered = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audioConnected = false;
        using var coordinator = new ConnectionCoordinator(
            () => audioConnected,
            () => false,
            async cancellationToken =>
            {
                var call = Interlocked.Increment(ref entered);
                if (call == 1)
                {
                    firstEntered.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                audioConnected = true;
                return new AudioSwitchResult(true, "Audio connected.", true);
            },
            _ => Task.FromResult(new AudioSwitchResult(true, "Audio disconnected.", false)),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);

        var first = coordinator.ConnectAudioAsync(TestContext.Current.CancellationToken);
        await firstEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        var second = coordinator.ConnectAudioAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref entered));
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, Volatile.Read(ref entered));
    }
}
