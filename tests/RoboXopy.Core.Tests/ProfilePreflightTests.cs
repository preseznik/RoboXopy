using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class ProfilePreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-preflight-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ReportsAvailableCapacityForReadyDestinationVolume()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var destination = Path.Combine(_root, "destination");
        var preflight = ProfilePreflight.Check(new SyncProfile { Source = source, Destination = destination });

        Assert.False(preflight.HasErrors);
        Assert.NotNull(preflight.DestinationFreeBytes);
        Assert.Contains(preflight.Findings, finding => finding.Severity == PreflightSeverity.Info);
    }

    [Fact]
    public void BlocksUnavailableDestinationVolume()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        var unusedLetter = Enumerable.Range('A', 26)
            .Select(Convert.ToChar)
            .First(letter => !usedLetters.Contains(letter));
        var preflight = ProfilePreflight.Check(new SyncProfile { Source = source, Destination = $@"{unusedLetter}:\missing\backup" });

        Assert.True(preflight.HasErrors);
        Assert.Contains(preflight.Findings, finding => finding.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlocksUnavailableSourceFolder()
    {
        var destination = Path.Combine(_root, "destination");
        var preflight = ProfilePreflight.Check(new SyncProfile { Source = Path.Combine(_root, "missing"), Destination = destination });

        Assert.True(preflight.HasErrors);
        Assert.Contains(preflight.Findings, finding => finding.Message.Contains("source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WarnsBeforeDestructiveDestinationOperation()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var profile = new SyncProfile { Source = source, Destination = Path.Combine(_root, "destination") };
        profile.Options.Mode = CopyMode.Mirror;

        var preflight = ProfilePreflight.Check(profile);

        Assert.True(preflight.HasWarnings);
        Assert.Contains(preflight.Findings, finding => finding.Message.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }
}
