using RoboXopy.Core.Models;

namespace RoboXopy.Core.Insights;

public enum HistoryMetric
{
    Bytes,
    Files,
}

/// <summary>One run as a chart slot. Values are in the series' metric (bytes or files).</summary>
public sealed record HistoryPoint(
    RunRecord Run,
    long Copied,
    long Removed,
    long? SourceTotal)
{
    public bool IsPreview => Run.DryRun;
    public bool HasSummary => Run.Summary is not null;
    public bool IsProblem => Run.Outcome is RunOutcome.Failed or RunOutcome.Error;
    public bool IsWarning => !IsProblem && (Run.Outcome == RunOutcome.Warning || Run.HasWarnings);
}

/// <summary>Totals for the real (non-preview) runs in a series.</summary>
public sealed record HistoryStats
{
    public int Runs { get; init; }
    public int Problems { get; init; }
    public long CopiedBytes { get; init; }
    public long CopiedFiles { get; init; }
    public long RemovedBytes { get; init; }
    public long RemovedFiles { get; init; }
    public TimeSpan? AverageDuration { get; init; }
    public long? SourceBytes { get; init; }
    public long? SourceFiles { get; init; }
    public long? SourceBytesChange { get; init; }
    public long? SourceFilesChange { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public sealed record HistorySeries(IReadOnlyList<HistoryPoint> Points, HistoryStats Stats, HistoryMetric Metric)
{
    public static readonly HistorySeries Empty = new([], new HistoryStats(), HistoryMetric.Bytes);

    /// <summary>
    /// Builds a chart series from history (any order). Skipped runs are ignored, previews only when
    /// <paramref name="includePreviews"/> is set. Keeps the newest <paramref name="maxRuns"/> slots
    /// (0 = all) and returns them oldest first.
    /// </summary>
    public static HistorySeries Build(IEnumerable<RunRecord> records, HistoryMetric metric, int maxRuns = 0, bool includePreviews = false)
    {
        var runs = records
            .Where(r => r.Outcome != RunOutcome.Skipped && (includePreviews || !r.DryRun))
            .OrderByDescending(r => r.StartedAt);
        var selected = (maxRuns > 0 ? runs.Take(maxRuns) : runs).Reverse().ToList();

        var points = selected.Select(r => ToPoint(r, metric)).ToList();
        return new HistorySeries(points, ComputeStats(selected), metric);
    }

    /// <summary>Number of real runs with a parsed summary – the chart needs at least two.</summary>
    public static int ChartableRuns(IEnumerable<RunRecord> records) =>
        records.Count(r => !r.DryRun && r.Summary is not null && r.Outcome != RunOutcome.Skipped);

    private static HistoryPoint ToPoint(RunRecord run, HistoryMetric metric)
    {
        if (run.Summary is not { } s) return new HistoryPoint(run, 0, 0, null);
        var row = metric == HistoryMetric.Bytes ? s.Bytes : s.Files;
        return new HistoryPoint(run, row.Copied, run.PurgesExtras ? row.Extras : 0, row.Total);
    }

    private static HistoryStats ComputeStats(IReadOnlyList<RunRecord> oldestFirst)
    {
        var real = oldestFirst.Where(r => !r.DryRun).ToList();
        if (real.Count == 0) return new HistoryStats();

        var withSummary = real.Where(r => r.Summary is not null).ToList();
        var finished = real.Where(r => r.Outcome != RunOutcome.Cancelled && r.EndedAt > r.StartedAt).ToList();
        var first = withSummary.FirstOrDefault()?.Summary;
        var last = withSummary.LastOrDefault()?.Summary;

        return new HistoryStats
        {
            Runs = real.Count,
            Problems = real.Count(r => r.Outcome is RunOutcome.Failed or RunOutcome.Error),
            CopiedBytes = withSummary.Sum(r => r.Summary!.Bytes.Copied),
            CopiedFiles = withSummary.Sum(r => r.Summary!.Files.Copied),
            RemovedBytes = withSummary.Where(r => r.PurgesExtras).Sum(r => r.Summary!.Bytes.Extras),
            RemovedFiles = withSummary.Where(r => r.PurgesExtras).Sum(r => r.Summary!.Files.Extras),
            AverageDuration = finished.Count > 0 ? TimeSpan.FromTicks((long)finished.Average(r => r.Duration.Ticks)) : null,
            SourceBytes = last?.Bytes.Total,
            SourceFiles = last?.Files.Total,
            SourceBytesChange = withSummary.Count >= 2 ? last!.Bytes.Total - first!.Bytes.Total : null,
            SourceFilesChange = withSummary.Count >= 2 ? last!.Files.Total - first!.Files.Total : null,
            From = real[0].StartedAt,
            To = real[^1].StartedAt,
        };
    }
}
