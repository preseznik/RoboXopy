using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class DestinationHealthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-health-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void CapturesCapacityForAvailableDestination()
    {
        var destination = Path.Combine(_root, "destination");
        var health = DestinationHealthProbe.Capture(destination);

        Assert.True(health.IsAvailable);
        Assert.NotNull(health.AvailableFreeBytes);
        Assert.Contains("free", health.Summary);
    }

    [Fact]
    public void ReportsUnavailableDestinationVolume()
    {
        var usedLetters = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var unusedLetter = Enumerable.Range('A', 26).Select(Convert.ToChar).First(letter => !usedLetters.Contains(letter));
        var health = DestinationHealthProbe.Capture($@"{unusedLetter}:\missing\backup");

        Assert.False(health.IsAvailable);
        Assert.Contains("unavailable", health.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
