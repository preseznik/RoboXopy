using System.Diagnostics;
using RoboXopy.Core.Insights;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public sealed class UnreadableFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // Windows may still be finishing the deletion of a junction or a folder, so retry briefly.
        for (var attempt = 0; attempt < 5 && Directory.Exists(_root); attempt++)
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }

    private static RunRecord Record(long failedDirs) => new()
    {
        ProfileId = Guid.Empty,
        StartedAt = DateTimeOffset.Now,
        EndedAt = DateTimeOffset.Now,
        Outcome = RunOutcome.Failed,
        Summary = new RobocopySummary { Dirs = new SummaryRow { Total = 10, Failed = failedDirs } },
    };

    private static IEnumerable<OutputLine> Lines(params string[] lines) => lines.Select(RobocopyOutputParser.Parse);

    private static string Dir(string path, int count = 0) => $"\t                   {count}\t{path}\\";

    private static Func<string, FolderAccess> Probe(Dictionary<string, FolderAccess> map) =>
        path => map.TryGetValue(path, out var access) ? access : FolderAccess.Readable;

    [Fact]
    public void NoFailedDirectories_ReturnsNull()
    {
        var finding = UnreadableFolders.Analyze(Record(0), Lines(Dir(@"C:\src\a")), redirectionGuard: true,
            probe: _ => throw new InvalidOperationException("should not probe"));
        Assert.Null(finding);
    }

    [Fact]
    public void NamesFoldersYourAccountCantOpen()
    {
        var probed = new List<string>();
        var finding = UnreadableFolders.Analyze(
            Record(2),
            Lines(
                Dir(@"C:\src"),
                Dir(@"C:\src\ok", count: 3),
                Dir(@"C:\src\.pytest_cache"),
                "\t*EXTRA Dir        -1\tC:\\dst\\gone\\",
                Dir(@"C:\src\empty"),
                Dir(@"C:\src\locked")),
            redirectionGuard: false,
            probe: path =>
            {
                probed.Add(path);
                return path.EndsWith("cache") || path.EndsWith("locked") ? FolderAccess.Unreadable : FolderAccess.Readable;
            });

        Assert.NotNull(finding);
        Assert.Equal(RunFindingKind.UnreadableFolders, finding.Kind);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains(@"couldn't open 2 source folders", finding.Message);
        Assert.Contains(@"C:\src\.pytest_cache, C:\src\locked.", finding.Message);
        Assert.Contains("fix their permissions", finding.Message);
        Assert.DoesNotContain("more", finding.Message);

        // Folders with files and extra destination folders aren't checked.
        Assert.Equal([@"C:\src", @"C:\src\.pytest_cache", @"C:\src\empty", @"C:\src\locked"], probed);
    }

    [Fact]
    public void LinksBlockedByRedirectionGuard_SayToRestart()
    {
        var map = new Dictionary<string, FolderAccess>
        {
            [@"C:\src\store\a"] = FolderAccess.UnreadableLink,
            [@"C:\src\store\b"] = FolderAccess.UnreadableLink,
        };

        var finding = UnreadableFolders.Analyze(Record(2), Lines(Dir(@"C:\src\store\a"), Dir(@"C:\src\store\b")), redirectionGuard: true, probe: Probe(map));

        Assert.NotNull(finding);
        Assert.Contains("junctions or symbolic links", finding.Message);
        Assert.Contains("start it again", finding.Message);
    }

    [Fact]
    public void BrokenLinksWithoutGuard_SayToCheckTargets()
    {
        var map = new Dictionary<string, FolderAccess> { [@"C:\src\link"] = FolderAccess.UnreadableLink };

        var finding = UnreadableFolders.Analyze(Record(1), Lines(Dir(@"C:\src\link")), redirectionGuard: false, probe: Probe(map));

        Assert.NotNull(finding);
        Assert.Contains("couldn't open 1 source folder,", finding.Message);
        Assert.Contains("check that their targets still exist", finding.Message);
        Assert.DoesNotContain("start it again", finding.Message);
    }

    [Fact]
    public void ManyFolders_NamesFirstFiveAndCountsTheRest()
    {
        var paths = Enumerable.Range(1, 8).Select(i => $@"C:\src\d{i}").ToArray();
        var finding = UnreadableFolders.Analyze(Record(12), Lines(paths.Select(p => Dir(p)).ToArray()), redirectionGuard: false,
            probe: _ => FolderAccess.Unreadable);

        Assert.NotNull(finding);
        Assert.Contains(@"C:\src\d5 and 7 more.", finding.Message);
        Assert.DoesNotContain(@"C:\src\d6", finding.Message);
    }

    [Fact]
    public void StopsProbingOnceAllFailuresAreFound()
    {
        var probes = 0;
        UnreadableFolders.Analyze(Record(1), Lines(Dir(@"C:\src\a"), Dir(@"C:\src\b"), Dir(@"C:\src\c")), redirectionGuard: false,
            probe: _ => { probes++; return FolderAccess.Unreadable; });
        Assert.Equal(1, probes);
    }

    [Fact]
    public void ProbesEachFolderOnce()
    {
        var probes = 0;
        UnreadableFolders.Analyze(Record(1), Lines(Dir(@"C:\src\a"), Dir(@"C:\src\a"), "\t  New Dir          0\tC:\\src\\a\\"),
            redirectionGuard: false, probe: _ => { probes++; return FolderAccess.Readable; });
        Assert.Equal(1, probes);
    }

    [Fact]
    public void FailuresRobocopyExplained_ReturnNull()
    {
        var finding = UnreadableFolders.Analyze(
            Record(1),
            Lines(Dir(@"C:\src\a"), "2026/10/10 00:17:32 ERROR 5 (0x00000005) Scanning Source Directory C:\\src\\b\\"),
            redirectionGuard: false,
            probe: _ => FolderAccess.Readable);
        Assert.Null(finding);
    }

    [Fact]
    public void NothingFound_UnderGuard_StillSaysToRestart()
    {
        var finding = UnreadableFolders.Analyze(Record(3), Lines(Dir(@"C:\src\a")), redirectionGuard: true, probe: _ => FolderAccess.Readable);

        Assert.NotNull(finding);
        Assert.Contains("couldn't open 3 source folders", finding.Message);
        Assert.Contains("start it again", finding.Message);
    }

    [Fact]
    public void NothingFound_WithoutErrors_PointsAtTheLog()
    {
        var finding = UnreadableFolders.Analyze(Record(2), Lines(Dir(@"C:\src\a")), redirectionGuard: false, probe: _ => FolderAccess.Missing);

        Assert.NotNull(finding);
        Assert.Contains("Check the log", finding.Message);
        Assert.True(finding.MayRequireElevation);
    }

    [Fact]
    public void BudgetStopsProbing()
    {
        var probes = 0;
        var lines = Lines(Dir(@"C:\src\a"), Dir(@"C:\src\b"), Dir(@"C:\src\c")).ToList();
        UnreadableFolders.Analyze(Record(5), lines, redirectionGuard: false,
            probe: _ => { probes++; Thread.Sleep(150); return FolderAccess.Readable; }, budget: TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, probes);
    }

    private static string WindowsUser => System.Security.Principal.WindowsIdentity.GetCurrent().Name;

    private static void Run(string file, string args)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static void DenyListing(string path) => Run("icacls.exe", $"\"{path}\" /deny \"{WindowsUser}:(OI)(CI)(RD)\"");

    private static void AllowListing(string path) => Run("icacls.exe", $"\"{path}\" /remove:d \"{WindowsUser}\"");

    private static void Junction(string link, string target) => Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"");

    private string[] Arguments(params string[] switches) => [_root, Path.Combine(Path.GetTempPath(), "roboxopy-dst"), .. switches];

    // Multithreaded runs list no folders, only files and the summary.
    private static IEnumerable<OutputLine> MultithreadedOutput() => Lines("\t    New File  \t\t       1\tC:\\src\\a.txt");

    [Fact]
    public void NoFoldersInOutput_WalksTheSourceTree()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_root, "a", "b", "locked")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "c"));
        DenyListing(locked);
        try
        {
            var finding = UnreadableFolders.Analyze(Record(1), MultithreadedOutput(), redirectionGuard: false, Arguments("/E", "/MT:8", "/XJ"));

            Assert.NotNull(finding);
            Assert.Contains(locked + ".", finding.Message);
            Assert.Contains("fix their permissions", finding.Message);
        }
        finally
        {
            AllowListing(locked);
        }
    }

    [Fact]
    public void Walk_FollowsRobocopysFolderSwitches()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_root, "deep", "locked")).FullName;
        DenyListing(locked);
        try
        {
            // Excluded by name, by full path, by wildcard; beyond /LEV; or no subfolders copied at all.
            foreach (var switches in new[]
                     {
                         new[] { "/E", "/XD", "locked" },
                         ["/MIR", "/XD", "other", locked + "\\"],
                         ["/S", "/XD", "lo*"],
                         ["/E", "/LEV:2"],
                         [],
                     })
            {
                var finding = UnreadableFolders.Analyze(Record(1), MultithreadedOutput(), redirectionGuard: false, Arguments(switches));
                Assert.NotNull(finding);
                Assert.DoesNotContain(locked, finding.Message);
                Assert.Contains("Check the log", finding.Message);
            }

            var found = UnreadableFolders.Analyze(Record(1), MultithreadedOutput(), redirectionGuard: false, Arguments("/E", "/LEV:3", "/XD", "other"));
            Assert.Contains(locked, found!.Message);
        }
        finally
        {
            AllowListing(locked);
        }
    }

    [Fact]
    public void Walk_ChecksLinksUnlessExcluded()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var link = Path.Combine(_root, "store", "link");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Junction(link, target);
        Directory.Delete(target);

        var found = UnreadableFolders.Analyze(Record(1), MultithreadedOutput(), redirectionGuard: false, Arguments("/E"));
        Assert.Contains(link + ".", found!.Message);
        Assert.Contains("junctions or symbolic links", found.Message);

        foreach (var exclude in new[] { "/XJ", "/XJD" })
        {
            var skipped = UnreadableFolders.Analyze(Record(1), MultithreadedOutput(), redirectionGuard: false, Arguments("/E", exclude));
            Assert.DoesNotContain(link, skipped!.Message);
        }
    }

    [Fact]
    public void OutputThatListsFolders_DoesNotWalk()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_root, "locked")).FullName;
        DenyListing(locked);
        try
        {
            var finding = UnreadableFolders.Analyze(Record(1), Lines(Dir(_root, count: 2)), redirectionGuard: false, Arguments("/E"));
            Assert.DoesNotContain(locked, finding!.Message);
        }
        finally
        {
            AllowListing(locked);
        }
    }

    [Fact]
    public void Probe_ClassifiesRealFolders()
    {
        var readable = Directory.CreateDirectory(Path.Combine(_root, "readable")).FullName;
        Assert.Equal(FolderAccess.Readable, UnreadableFolders.Probe(readable));
        Assert.Equal(FolderAccess.Missing, UnreadableFolders.Probe(Path.Combine(_root, "missing")));

        // A junction whose target was deleted.
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var junction = Path.Combine(_root, "junction");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
               { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }

        Assert.Equal(FolderAccess.Readable, UnreadableFolders.Probe(junction));
        Directory.Delete(target);
        Assert.Equal(FolderAccess.UnreadableLink, UnreadableFolders.Probe(junction));
    }
}
