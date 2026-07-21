using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace WUBT10Fix;

internal sealed class DiagnosticLogWriter : IDisposable
{
    internal const long DefaultMaximumFileBytes = 2 * 1024 * 1024;
    internal const int DefaultMaximumFiles = 20;

    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _sessionId;
    private readonly long _maximumFileBytes;
    private readonly int _maximumFiles;
    private int _segment;
    private long _sequence;
    private bool _disposed;

    internal DiagnosticLogWriter(
        string directory,
        string sessionId,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumFiles = DefaultMaximumFiles)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A diagnostic directory is required.", nameof(directory));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("A diagnostic session ID is required.", nameof(sessionId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFiles);

        _directory = directory;
        _sessionId = SanitizeFileName(sessionId);
        _maximumFileBytes = maximumFileBytes;
        _maximumFiles = maximumFiles;
        Directory.CreateDirectory(_directory);
        CurrentPath = CreatePath();
        EnsureCurrentFileExists();
        PruneOldFiles();
    }

    internal string CurrentPath { get; private set; }

    internal void Write(string category, string eventName, IReadOnlyDictionary<string, object?>? data = null)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            try
            {
                RotateIfNeeded();
                var record = new DiagnosticRecord(
                    DateTimeOffset.UtcNow,
                    _sessionId,
                    Interlocked.Increment(ref _sequence),
                    category,
                    eventName,
                    data);
                File.AppendAllText(CurrentPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never affect connection or shutdown behavior.
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (File.Exists(CurrentPath) && new FileInfo(CurrentPath).Length >= _maximumFileBytes)
        {
            _segment++;
            CurrentPath = CreatePath();
            EnsureCurrentFileExists();
            PruneOldFiles();
        }
    }

    private string CreatePath() => Path.Combine(
        _directory,
        $"diagnostic-{_sessionId}-{_segment:D2}.jsonl");

    private void EnsureCurrentFileExists()
    {
        using var stream = new FileStream(CurrentPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
    }

    private void PruneOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(_directory)
                .GetFiles("diagnostic-*.jsonl")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(_maximumFiles)
                .ToArray();

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // A previous process may still own a retained diagnostic file.
                }
            }
        }
        catch
        {
            // Retention failure must not prevent new diagnostics.
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }

    public void Dispose()
    {
        lock (_sync)
            _disposed = true;
    }

    private sealed record DiagnosticRecord(
        DateTimeOffset TimestampUtc,
        string SessionId,
        long Sequence,
        string Category,
        string Event,
        IReadOnlyDictionary<string, object?>? Data);
}

internal static class DiagnosticTrace
{
    private static readonly object Sync = new();
    private static DiagnosticLogWriter? _writer;

    internal static string SessionId { get; private set; } = string.Empty;
    internal static string? CurrentPath
    {
        get
        {
            lock (Sync)
                return _writer?.CurrentPath;
        }
    }

    internal static void Begin(string mode)
    {
        var started = false;
        lock (Sync)
        {
            if (_writer is not null)
                return;

            SessionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-p{Environment.ProcessId}-{Guid.NewGuid():N}";
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WU-BT10-Fix",
                    "diagnostics");
                _writer = new DiagnosticLogWriter(directory, SessionId);
                started = true;
            }
            catch
            {
                // A read-only or damaged profile must not stop the application.
                _writer = null;
            }
        }

        if (started)
        {
            Write("process", "started", new Dictionary<string, object?>
            {
                ["mode"] = mode,
                ["productVersion"] = Application.ProductVersion,
                ["processId"] = Environment.ProcessId,
                ["os"] = RuntimeInformation.OSDescription,
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["administrator"] = IsAdministrator()
            });
        }
    }

    internal static void Write(string category, string eventName, IReadOnlyDictionary<string, object?>? data = null)
    {
        EnsureStarted();
        lock (Sync)
            _writer?.Write(category, eventName, data);
    }

    internal static void End(string reason)
    {
        Write("process", "ending", new Dictionary<string, object?> { ["reason"] = reason });
        lock (Sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void EnsureStarted()
    {
        lock (Sync)
        {
            if (_writer is not null)
                return;
        }

        Begin("implicit");
    }

    private static bool IsAdministrator()
    {
        try
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
