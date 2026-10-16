using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Tests;

public sealed class LogFileAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void CompressesAndReadsLog()
    {
        Directory.CreateDirectory(_root);
        var log = Path.Combine(_root, "run.log");
        File.WriteAllText(log, "first\r\nsecond\r\n");

        var compressed = LogFileAccess.Compress(log);

        Assert.EndsWith(".log.gz", compressed, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(log));
        Assert.Equal(["first", "second"], LogFileAccess.ReadLines(compressed).ToList());
    }

    [Fact]
    public void RotationRemovesExpiredCompressedLogs()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"));
        paths.EnsureCreated();
        var profileId = Guid.NewGuid();
        var dir = paths.ProfileLogsDirectory(profileId);
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "old.log.gz");
        File.WriteAllText(old, "old");
        File.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-31));
        var store = new HistoryStore(paths);

        store.ApplyLogRetention(profileId, keep: 30, maxAgeDays: 30);

        Assert.False(File.Exists(old));
    }
}
