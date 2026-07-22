using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;

namespace WUBT10Fix;

internal sealed record AudioPlaybackTestResult(
    bool Success,
    string Message,
    string? DeviceName = null,
    string? DeviceId = null);

internal static class AudioPlaybackTester
{
    internal static async Task<AudioPlaybackTestResult> PlayAsync(
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        var matches = devices
            .Where(device => device.Name.Contains("WU-BT10 AUDIO", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            return new(
                false,
                $"Expected one active WU-BT10 playback device, but Windows returned {matches.Length}.");
        }

        var device = matches[0];
        var testDuration = duration ?? TimeSpan.FromMilliseconds(900);
        var wavePath = Path.Combine(Path.GetTempPath(), $"wubt10-audio-test-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(wavePath, CreateTestWave(testDuration), cancellationToken);
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(wavePath);
            using var source = MediaSource.CreateFromStorageFile(file);
            using var player = new MediaPlayer
            {
                AudioDevice = device,
                AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia,
                Source = source,
                Volume = 1.0
            };

            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaEnded += (_, _) => completed.TrySetResult(true);
            player.MediaFailed += (_, args) => completed.TrySetException(
                new InvalidOperationException($"Windows playback failed: {args.Error} {args.ErrorMessage}"));

            player.Play();
            await completed.Task.WaitAsync(testDuration + TimeSpan.FromSeconds(8), cancellationToken);
            return new(
                true,
                "Windows rendered the test signal through the WU-BT10 playback endpoint.",
                device.Name,
                device.Id);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message, device.Name, device.Id);
        }
        finally
        {
            try
            {
                File.Delete(wavePath);
            }
            catch
            {
                // A stale test wave contains only a short generated tone.
            }
        }
    }

    internal static byte[] CreateTestWave(TimeSpan duration)
    {
        const int sampleRate = 48000;
        const short channels = 2;
        const short bitsPerSample = 16;
        const double frequency = 440.0;
        const double amplitude = 0.08;

        var sampleCount = Math.Max(1, checked((int)Math.Round(duration.TotalSeconds * sampleRate)));
        var blockAlign = checked((short)(channels * bitsPerSample / 8));
        var dataLength = checked(sampleCount * blockAlign);
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        var fadeSamples = Math.Min(sampleCount / 2, sampleRate / 50);
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var fadeIn = fadeSamples == 0 ? 1.0 : Math.Min(1.0, sample / (double)fadeSamples);
            var fadeOut = fadeSamples == 0 ? 1.0 : Math.Min(1.0, (sampleCount - 1 - sample) / (double)fadeSamples);
            var value = checked((short)Math.Round(
                short.MaxValue * amplitude * Math.Min(fadeIn, fadeOut) * Math.Sin(2 * Math.PI * frequency * sample / sampleRate)));
            writer.Write(value);
            writer.Write(value);
        }

        return stream.ToArray();
    }
}
