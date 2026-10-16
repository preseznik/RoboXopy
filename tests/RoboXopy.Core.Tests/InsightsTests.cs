using System.Globalization;
using System.Text.Json;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Insights;
using RoboXopy.Core.Models;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Tests;

public sealed class InsightsTests : IDisposable
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static RunRecord Run(int day, long total, long copied = 0, long extras = 0, bool mirror = true, bool dryRun = false,
        RunOutcome outcome = RunOutcome.Success, long bytesPerFile = 1000) => new()
        {
            ProfileId = Guid.Empty,
            StartedAt = T0.AddDays(day),
            EndedAt = T0.AddDays(day).AddMinutes(2),
            DryRun = dryRun,
            Outcome = outcome,
            PurgesExtras = mirror,
            Summary = new RobocopySummary
            {
                Files = new SummaryRow { Total = total, Copied = copied, Skipped = total - copied, Extras = extras },
                Bytes = new SummaryRow { Total = total * bytesPerFile, Copied = copied * bytesPerFile, Skipped = (total - copied) * bytesPerFile, Extras = extras * bytesPerFile },
            },
        };

    /// <summary>Ten normal days: ~10,000 files, a few copied and removed.</summary>
    private static List<RunRecord> NormalHistory(bool mirror = true) =>
        Enumerable.Range(0, 10).Select(d => Run(d, 10_000 + d * 10, copied: 20 + d, extras: d % 3, mirror: mirror)).ToList();

    [Fact]
    public void NormalRun_HasNoFindings()
    {
        var history = NormalHistory();
        var current = Run(11, 10_120, copied: 25, extras: 2);
        Assert.Null(RunInsights.Analyze(current, history, En));
    }

    [Fact]
    public void FirstRun_WithoutHistory_HasNoFindings()
    {
        Assert.Null(RunInsights.Analyze(Run(0, 5000, copied: 5000), [], En));
    }

    [Fact]
    public void LargeRemoval_AgainstHistory_IsWarning()
    {
        var current = Run(11, 6_000, copied: 0, extras: 4_213, dryRun: true);
        var findings = RunInsights.Analyze(current, NormalHistory(), En);

        var removal = Assert.Single(findings!, f => f.Kind == RunFindingKind.LargeRemoval);
        Assert.Equal(FindingSeverity.Warning, removal.Severity);
        Assert.Equal("Would remove 4,213 files (4 MB), 41% of the destination. Earlier runs removed at most 2.", removal.Message);
        Assert.True(current.Findings is null);
    }

    [Fact]
    public void LargeRemoval_WithoutBaseline_UsesShareOfDestination()
    {
        var small = RunInsights.Analyze(Run(0, 1_000, extras: 60), [], En);
        Assert.Null(small); // 60 of 1,060 = 5.7%

        var large = RunInsights.Analyze(Run(0, 1_000, extras: 200), [], En);
        var finding = Assert.Single(large!);
        Assert.Equal(RunFindingKind.LargeRemoval, finding.Kind);
        Assert.StartsWith("Removed 200 files", finding.Message);
        Assert.DoesNotContain("Earlier runs", finding.Message);
    }

    [Fact]
    public void Removal_BelowFloor_IsIgnored()
    {
        // 49 files is 100% of the destination but below the absolute floor.
        Assert.Null(RunInsights.Analyze(Run(11, 0, extras: 49), [], En));
    }

    [Fact]
    public void Removal_InCopyMode_IsNeverReported()
    {
        var current = Run(11, 10_000, extras: 5_000, mirror: false);
        var findings = RunInsights.Analyze(current, NormalHistory(mirror: false), En);
        Assert.DoesNotContain(findings ?? [], f => f.Kind == RunFindingKind.LargeRemoval);
    }

    [Fact]
    public void RoutinelyLargeRemovals_AreNotUnusual()
    {
        // A cache-like folder where a quarter of the destination churns every run.
        var history = Enumerable.Range(0, 10).Select(d => Run(d, 3_000, copied: 1_000, extras: 1_000 + d * 10)).ToList();
        Assert.Null(RunInsights.Analyze(Run(11, 3_000, copied: 1_000, extras: 1_050), history, En));
    }

    [Fact]
    public void SmallSpike_InHugeDestination_IsNotUnusual()
    {
        // 80 removed where earlier runs removed at most 2, but it's well under 1% of 10,000 files.
        Assert.Null(RunInsights.Analyze(Run(11, 10_100, extras: 80), NormalHistory(), En));
    }

    [Fact]
    public void HalfOfDestination_IsAlwaysUnusual()
    {
        var history = Enumerable.Range(0, 10).Select(d => Run(d, 1_000, extras: 400)).ToList();
        var findings = RunInsights.Analyze(Run(11, 1_000, extras: 1_200), history, En);
        Assert.Contains(findings!, f => f.Kind == RunFindingKind.LargeRemoval);
    }

    [Fact]
    public void EmptySource_IsReported_EvenWithShortHistory()
    {
        var history = new List<RunRecord> { Run(0, 10_000) };
        var current = Run(1, 0, extras: 10_000, dryRun: true);
        var findings = RunInsights.Analyze(current, history, En)!;

        var shrank = Assert.Single(findings, f => f.Kind == RunFindingKind.SourceShrank);
        Assert.Equal(FindingSeverity.Warning, shrank.Severity);
        Assert.Equal("The source has no files, but it usually has about 10,000. Check that the drive is connected and the path is right.", shrank.Message);
        Assert.Contains(findings, f => f.Kind == RunFindingKind.LargeRemoval);
    }

    [Fact]
    public void SourceShrank_ByAThird_IsReported()
    {
        var findings = RunInsights.Analyze(Run(11, 6_500, extras: 30), NormalHistory(), En)!;
        var shrank = Assert.Single(findings);
        Assert.Equal(RunFindingKind.SourceShrank, shrank.Kind);
        Assert.Equal("The source has 6,500 files, 35% fewer than usual (about 10,045).", shrank.Message);
    }

    [Fact]
    public void SourceShrank_InCopyMode_IsOnlyInfo()
    {
        var findings = RunInsights.Analyze(Run(11, 0, mirror: false), NormalHistory(mirror: false), En)!;
        Assert.Equal(FindingSeverity.Info, Assert.Single(findings).Severity);
    }

    [Fact]
    public void SmallSource_ShrinkBelowFloor_IsIgnored()
    {
        var history = Enumerable.Range(0, 5).Select(d => Run(d, 200)).ToList();
        Assert.Null(RunInsights.Analyze(Run(6, 120), history, En)); // -40% but only 80 files
    }

    [Fact]
    public void LargeChange_IsInfo()
    {
        var current = Run(11, 12_000, copied: 2_000, bytesPerFile: 2_000_000);
        var history = Enumerable.Range(0, 10).Select(d => Run(d, 10_000, copied: 20, bytesPerFile: 2_000_000)).ToList();
        var findings = RunInsights.Analyze(current, history, En)!;

        var change = Assert.Single(findings);
        Assert.Equal(RunFindingKind.LargeChange, change.Kind);
        Assert.Equal(FindingSeverity.Info, change.Severity);
        Assert.False(current.HasWarnings);
    }

    [Fact]
    public void Baseline_IgnoresPreviewsFailuresOtherModesAndNewerRuns()
    {
        var current = Run(10, 1_000);
        var records = new List<RunRecord>
        {
            Run(1, 1_000),
            Run(2, 1_000, dryRun: true),
            Run(3, 0, outcome: RunOutcome.Failed),
            Run(4, 1_000, mirror: false),
            Run(11, 1_000),
            new() { StartedAt = T0.AddDays(5), Outcome = RunOutcome.Error, PurgesExtras = true },
            Run(6, 1_000, outcome: RunOutcome.Warning),
        };

        var baseline = RunInsights.Baseline(current, records);
        Assert.Equal([T0.AddDays(6), T0.AddDays(1)], baseline.Select(r => r.StartedAt));
    }

    [Fact]
    public void Analyze_SkipsRunsWithoutSummary()
    {
        var current = new RunRecord { StartedAt = T0, Outcome = RunOutcome.Error, PurgesExtras = true };
        Assert.Null(RunInsights.Analyze(current, NormalHistory(), En));
    }

    [Fact]
    public void Percentile_And_Median()
    {
        Assert.Equal(0, RunInsights.Percentile([], 0.9));
        Assert.Equal(9, RunInsights.Percentile([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 0.9));
        Assert.Equal(5, RunInsights.Median([9, 1, 5]));
        Assert.Equal(3, RunInsights.Median([1, 2, 4, 9]));
    }

    [Fact]
    public void Findings_RoundTripThroughHistory_AndOldLinesStillLoad()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"));
        paths.EnsureCreated();
        var store = new HistoryStore(paths);
        var profileId = Guid.NewGuid();

        var plain = Run(0, 100);
        plain.ProfileId = profileId;
        store.Append(plain);
        Assert.DoesNotContain("findings", File.ReadAllText(paths.HistoryFile(profileId)));

        var flagged = Run(1, 0, extras: 100);
        flagged.ProfileId = profileId;
        flagged.Findings = [new RunFinding { Kind = RunFindingKind.LargeRemoval, Severity = FindingSeverity.Warning, Message = "x" }];
        store.Append(flagged);

        var json = File.ReadAllLines(paths.HistoryFile(profileId))[1];
        Assert.Contains("\"kind\":\"LargeRemoval\"", json);
        Assert.Contains("\"severity\":\"Warning\"", json);

        var loaded = store.Load(profileId);
        Assert.True(loaded[0].HasWarnings);
        Assert.Equal(RunFindingKind.LargeRemoval, Assert.Single(loaded[0].Findings!).Kind);
        Assert.Null(loaded[1].Findings);
        Assert.False(loaded[1].HasWarnings);

        var legacy = JsonSerializer.Deserialize<RunRecord>("""{"id":"00000000-0000-0000-0000-000000000001","outcome":"Success","purgesExtras":true}""", JsonDefaults.Options);
        Assert.Null(legacy!.Findings);
    }

    [Fact]
    public async Task Executor_FlagsUnusualMirrorRemoval()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dst = Path.Combine(_root, "dst");
        for (var i = 0; i < 60; i++) File.WriteAllText(Path.Combine(src, $"f{i}.txt"), "x");

        var paths = new AppPaths(Path.Combine(_root, "data"));
        var history = new HistoryStore(paths);
        var executor = new ProfileExecutor(paths, history, () => new AppSettings());
        var profile = new SyncProfile { Name = "Mirror", Source = src, Destination = dst };
        profile.Options.Mode = CopyMode.Mirror;

        for (var i = 0; i < 3; i++)
        {
            var normal = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual));
            Assert.Null(normal.Findings);
        }

        foreach (var file in Directory.GetFiles(src)) File.Delete(file);

        var preview = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual, DryRun: true));
        Assert.True(preview.HasWarnings);
        Assert.Contains(preview.Findings!, f => f.Kind == RunFindingKind.SourceShrank);
        Assert.Contains(preview.Findings!, f => f.Kind == RunFindingKind.LargeRemoval);
        Assert.True(history.LoadLatest(profile.Id)!.HasWarnings);
        Assert.Contains("unusual", string.Join(Environment.NewLine, LogFileAccess.ReadLines(preview.LogFile!)));
        Assert.Equal(60, Directory.GetFiles(dst).Length);
    }
}

public sealed class HistorySeriesTests
{
    private static readonly DateTimeOffset T0 = new(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static RunRecord Run(int day, long total, long copied, long extras, bool mirror = true, bool dryRun = false,
        RunOutcome outcome = RunOutcome.Success, int minutes = 2) => new()
        {
            StartedAt = T0.AddDays(day),
            EndedAt = T0.AddDays(day).AddMinutes(minutes),
            DryRun = dryRun,
            Outcome = outcome,
            PurgesExtras = mirror,
            Summary = new RobocopySummary
            {
                Files = new SummaryRow { Total = total, Copied = copied, Extras = extras },
                Bytes = new SummaryRow { Total = total * 10, Copied = copied * 10, Extras = extras * 10 },
            },
        };

    [Fact]
    public void Build_OrdersOldestFirst_AndPicksMetric()
    {
        var records = new[] { Run(2, 120, 20, 3), Run(0, 100, 100, 0), Run(1, 105, 5, 1) };

        var files = HistorySeries.Build(records, HistoryMetric.Files);
        Assert.Equal([100L, 5, 20], files.Points.Select(p => p.Copied));
        Assert.Equal([0L, 1, 3], files.Points.Select(p => p.Removed));
        Assert.Equal([100L, 105, 120], files.Points.Select(p => p.SourceTotal!.Value));

        var bytes = HistorySeries.Build(records, HistoryMetric.Bytes);
        Assert.Equal([1000L, 50, 200], bytes.Points.Select(p => p.Copied));
        Assert.Equal(HistoryMetric.Bytes, bytes.Metric);
    }

    [Fact]
    public void Build_KeepsNewestRuns_AndHonoursPreviewToggle()
    {
        var records = Enumerable.Range(0, 10).Select(d => Run(d, 100, d, 0, dryRun: d == 9)).ToList();
        records.Add(new RunRecord { StartedAt = T0.AddDays(20), Outcome = RunOutcome.Skipped });

        var last3 = HistorySeries.Build(records, HistoryMetric.Files, maxRuns: 3);
        Assert.Equal([6L, 7, 8], last3.Points.Select(p => p.Copied));

        var withPreviews = HistorySeries.Build(records, HistoryMetric.Files, maxRuns: 3, includePreviews: true);
        Assert.Equal([7L, 8, 9], withPreviews.Points.Select(p => p.Copied));
        Assert.True(withPreviews.Points[^1].IsPreview);
        Assert.Equal(2, withPreviews.Stats.Runs);
    }

    [Fact]
    public void Removed_OnlyCountsWhenPurging()
    {
        var series = HistorySeries.Build([Run(0, 100, 0, 7, mirror: false), Run(1, 100, 0, 7)], HistoryMetric.Files);
        Assert.Equal([0L, 7], series.Points.Select(p => p.Removed));
        Assert.Equal(7, series.Stats.RemovedFiles);
    }

    [Fact]
    public void Points_FlagProblemsAndWarnings()
    {
        var failed = Run(0, 100, 1, 0, outcome: RunOutcome.Failed);
        var error = new RunRecord { StartedAt = T0.AddDays(1), Outcome = RunOutcome.Error };
        var warned = Run(2, 100, 1, 0);
        warned.Findings = [new RunFinding { Severity = FindingSeverity.Warning }];
        var noted = Run(3, 100, 1, 0);
        noted.Findings = [new RunFinding { Severity = FindingSeverity.Info }];

        var p = HistorySeries.Build([failed, error, warned, noted], HistoryMetric.Files).Points;
        Assert.True(p[0].IsProblem);
        Assert.True(p[1].IsProblem);
        Assert.False(p[1].HasSummary);
        Assert.Null(p[1].SourceTotal);
        Assert.True(p[2].IsWarning);
        Assert.False(p[3].IsWarning);
    }

    [Fact]
    public void Stats_SummariseRealRuns()
    {
        var records = new[]
        {
            Run(0, 100, 100, 0, minutes: 4),
            Run(1, 110, 10, 2, minutes: 2),
            Run(2, 0, 0, 0, dryRun: true),
            new RunRecord { StartedAt = T0.AddDays(3), EndedAt = T0.AddDays(3).AddSeconds(1), Outcome = RunOutcome.Error },
            Run(4, 130, 20, 0, minutes: 3),
        };

        var stats = HistorySeries.Build(records, HistoryMetric.Bytes, includePreviews: true).Stats;
        Assert.Equal(4, stats.Runs);
        Assert.Equal(1, stats.Problems);
        Assert.Equal(130, stats.CopiedFiles);
        Assert.Equal(1300, stats.CopiedBytes);
        Assert.Equal(2, stats.RemovedFiles);
        Assert.Equal(130, stats.SourceFiles);
        Assert.Equal(30, stats.SourceFilesChange);
        Assert.Equal(300, stats.SourceBytesChange);
        Assert.Equal(TimeSpan.FromSeconds((240 + 120 + 1 + 180) / 4.0), stats.AverageDuration);
        Assert.Equal(T0, stats.From);
        Assert.Equal(T0.AddDays(4), stats.To);
    }

    [Fact]
    public void Stats_EmptyWhenOnlyPreviews()
    {
        var stats = HistorySeries.Build([Run(0, 1, 1, 0, dryRun: true)], HistoryMetric.Files, includePreviews: true).Stats;
        Assert.Equal(0, stats.Runs);
        Assert.Null(stats.SourceFiles);
    }

    [Fact]
    public void ChartableRuns_CountsRealRunsWithSummary()
    {
        RunRecord[] records = [Run(0, 1, 1, 0), Run(1, 1, 1, 0, dryRun: true), new RunRecord { Outcome = RunOutcome.Error }, Run(2, 1, 1, 0)];
        Assert.Equal(2, HistorySeries.ChartableRuns(records));
    }
}
