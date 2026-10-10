using System.Globalization;
using RoboXopy.Core.Formatting;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Insights;

/// <summary>
/// Compares a run with the profile's own recent history and reports anything unusual – most
/// importantly a mirror about to remove far more than it normally does, or a source that
/// suddenly shrank (disconnected drive, renamed folder).
/// </summary>
public static class RunInsights
{
    /// <summary>How many recent real runs make up the "normal" baseline.</summary>
    public const int BaselineSize = 20;

    /// <summary>History-based rules need at least this many baseline runs.</summary>
    public const int MinBaselineRuns = 3;

    /// <summary>Removals below this many files are never reported.</summary>
    public const long RemovalFloorFiles = 50;

    /// <summary>Without a baseline, removing at least this share of the destination is unusual.</summary>
    public const double RemovalShareWithoutBaseline = 0.10;

    /// <summary>Removing at least this share of the destination is always unusual.</summary>
    public const double RemovalShareAlways = 0.50;

    /// <summary>With a baseline, a removal spike must also be at least this share of the destination.</summary>
    public const double RemovalShareMinimum = 0.01;

    /// <summary>A removal spike is this many times the baseline's 90th percentile.</summary>
    public const double RemovalSpikeFactor = 5;

    /// <summary>The source shrinking by at least this fraction of its usual file count is unusual.</summary>
    public const double ShrinkFraction = 0.30;

    /// <summary>A shrink must also be at least this many files.</summary>
    public const long ShrinkFloorFiles = 100;

    /// <summary>Copying this many times the baseline's 90th percentile is unusual.</summary>
    public const double ChangeSpikeFactor = 10;

    public const long ChangeFloorBytes = 1L << 30;
    public const long ChangeFloorFiles = 1000;

    /// <summary>The recent real runs that define what is normal for <paramref name="current"/>, newest first.</summary>
    public static IReadOnlyList<RunRecord> Baseline(RunRecord current, IEnumerable<RunRecord> previous) =>
        previous
            .Where(r => r.Id != current.Id
                        && !r.DryRun
                        && r.Summary is not null
                        && r.PurgesExtras == current.PurgesExtras
                        && r.Outcome is RunOutcome.Success or RunOutcome.Warning
                        && r.StartedAt <= current.StartedAt)
            .OrderByDescending(r => r.StartedAt)
            .Take(BaselineSize)
            .ToList();

    /// <summary>Returns the findings for <paramref name="current"/>, or null when nothing is unusual.</summary>
    public static List<RunFinding>? Analyze(RunRecord current, IEnumerable<RunRecord> previous, CultureInfo? culture = null)
    {
        if (current.Summary is not { } summary || current.Outcome is RunOutcome.Error or RunOutcome.Skipped) return null;

        culture ??= CultureInfo.CurrentCulture;
        var baseline = Baseline(current, previous);
        var findings = new List<RunFinding>();

        if (CheckSourceShrank(current, summary, baseline, culture) is { } shrank) findings.Add(shrank);
        if (CheckLargeRemoval(current, summary, baseline, culture) is { } removal) findings.Add(removal);
        if (CheckLargeChange(current, summary, baseline, culture) is { } change) findings.Add(change);

        return findings.Count > 0 ? findings : null;
    }

    private static RunFinding? CheckSourceShrank(RunRecord current, RobocopySummary summary, IReadOnlyList<RunRecord> baseline, CultureInfo culture)
    {
        if (baseline.Count == 0) return null;

        var usual = Median(baseline.Select(r => r.Summary!.Files.Total));
        if (usual <= 0) return null;

        var total = summary.Files.Total;
        var severity = current.PurgesExtras ? FindingSeverity.Warning : FindingSeverity.Info;

        if (total == 0)
        {
            return new RunFinding
            {
                Kind = RunFindingKind.SourceShrank,
                Severity = severity,
                Message = string.Create(culture,
                    $"The source has no files, but it usually has about {usual:N0}. Check that the drive is connected and the path is right."),
            };
        }

        if (baseline.Count < MinBaselineRuns) return null;

        var drop = usual - total;
        if (drop < ShrinkFloorFiles || drop < usual * ShrinkFraction) return null;

        return new RunFinding
        {
            Kind = RunFindingKind.SourceShrank,
            Severity = severity,
            Message = string.Create(culture,
                $"The source has {total:N0} files, {Percent(drop / (double)usual)} fewer than usual (about {usual:N0})."),
        };
    }

    private static RunFinding? CheckLargeRemoval(RunRecord current, RobocopySummary summary, IReadOnlyList<RunRecord> baseline, CultureInfo culture)
    {
        if (!current.PurgesExtras) return null;

        var removed = summary.Files.Extras;
        if (removed < RemovalFloorFiles) return null;

        // Source files plus extras approximates the destination from above, so the share is a lower bound.
        var share = removed / (double)Math.Max(1, summary.Files.Total + removed);
        var hasBaseline = baseline.Count >= MinBaselineRuns;
        var usualMax = hasBaseline ? baseline.Max(r => r.Summary!.Files.Extras) : 0;

        var usualP90 = hasBaseline ? Percentile(baseline.Select(r => r.Summary!.Files.Extras), 0.9) : 0;
        var unusual = share >= RemovalShareAlways
                      || (hasBaseline
                          ? removed > Math.Max(RemovalSpikeFactor * usualP90, 2.0 * usualMax) && share >= RemovalShareMinimum
                          : share >= RemovalShareWithoutBaseline);
        if (!unusual) return null;

        var verb = current.DryRun ? "Would remove" : "Removed";
        var message = string.Create(culture,
            $"{verb} {removed:N0} files ({ByteSize.Format(summary.Bytes.Extras, culture)}), {Percent(share)} of the destination.");
        if (hasBaseline)
        {
            message += usualMax == 0
                ? " Earlier runs didn't remove any files."
                : string.Create(culture, $" Earlier runs removed at most {usualMax:N0}.");
        }

        return new RunFinding { Kind = RunFindingKind.LargeRemoval, Severity = FindingSeverity.Warning, Message = message };
    }

    private static RunFinding? CheckLargeChange(RunRecord current, RobocopySummary summary, IReadOnlyList<RunRecord> baseline, CultureInfo culture)
    {
        if (baseline.Count < MinBaselineRuns) return null;

        var files = summary.Files.Copied;
        var bytes = summary.Bytes.Copied;
        var usualFiles = Percentile(baseline.Select(r => r.Summary!.Files.Copied), 0.9);
        var usualBytes = Percentile(baseline.Select(r => r.Summary!.Bytes.Copied), 0.9);

        var spike = (bytes >= ChangeFloorBytes && bytes > ChangeSpikeFactor * usualBytes)
                    || (files >= ChangeFloorFiles && files > ChangeSpikeFactor * usualFiles);
        if (!spike) return null;

        var verb = current.DryRun ? "Would copy" : "Copied";
        return new RunFinding
        {
            Kind = RunFindingKind.LargeChange,
            Severity = FindingSeverity.Info,
            Message = string.Create(culture,
                $"{verb} {files:N0} files ({ByteSize.Format(bytes, culture)}), far more than usual (typically up to {usualFiles:N0} files, {ByteSize.Format(usualBytes, culture)})."),
        };
    }

    private static string Percent(double fraction) =>
        fraction < 0.01 ? "<1%" : $"{Math.Round(fraction * 100):0}%";

    /// <summary>Nearest-rank percentile.</summary>
    internal static long Percentile(IEnumerable<long> values, double p)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    internal static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
