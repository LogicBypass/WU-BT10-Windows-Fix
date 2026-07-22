namespace WUBT10Fix;

internal sealed class AudioOperationLease : IDisposable
{
    private readonly FileStream? _stream;

    private AudioOperationLease(FileStream? stream)
    {
        _stream = stream;
    }

    internal bool IsAcquired => _stream is not null;

    internal static AudioOperationLease TryAcquire()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WU-BT10-Fix");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "audio-operation.lock");
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}");
            writer.Flush();
            stream.Flush(true);
            stream.Position = 0;
            return new(stream);
        }
        catch (IOException)
        {
            return new(null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(null);
        }
    }

    public void Dispose() => _stream?.Dispose();
}
