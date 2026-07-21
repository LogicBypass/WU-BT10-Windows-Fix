using System.Text.Json;

namespace WUBT10Fix.Tests;

public sealed class DiagnosticLogWriterTests
{
    [Fact]
    public void WriteProducesStructuredJsonLineWithCorrelationFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var writer = new DiagnosticLogWriter(directory, "test-session");
            writer.Write("midi", "gatt-opened", new Dictionary<string, object?>
            {
                ["status"] = "Active",
                ["count"] = 2
            });

            var line = Assert.Single(File.ReadAllLines(writer.CurrentPath));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Equal("test-session", root.GetProperty("SessionId").GetString());
            Assert.Equal(1, root.GetProperty("Sequence").GetInt64());
            Assert.Equal("midi", root.GetProperty("Category").GetString());
            Assert.Equal("gatt-opened", root.GetProperty("Event").GetString());
            Assert.Equal("Active", root.GetProperty("Data").GetProperty("status").GetString());
            Assert.Equal(2, root.GetProperty("Data").GetProperty("count").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteRotatesAndRetainsOnlyConfiguredNumberOfFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var writer = new DiagnosticLogWriter(
                directory,
                "rotation-session",
                maximumFileBytes: 1,
                maximumFiles: 2);

            for (var index = 0; index < 5; index++)
                writer.Write("test", "record", new Dictionary<string, object?> { ["index"] = index });

            var files = Directory.GetFiles(directory, "diagnostic-*.jsonl");
            Assert.Equal(2, files.Length);
            Assert.Contains("-04.jsonl", writer.CurrentPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(writer.CurrentPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DisposePreventsAdditionalRecords()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var writer = new DiagnosticLogWriter(directory, "dispose-session");
            writer.Write("test", "before-dispose");
            var path = writer.CurrentPath;
            var before = File.ReadAllText(path);

            writer.Dispose();
            writer.Write("test", "after-dispose");

            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wubt10-diagnostic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
