using System.Globalization;

namespace RoboXopy.Core.Models;

public enum StaleSyncLevel
{
    Fresh,
    Stale,
    Overdue,
}

/// <summary>Freshness of a profile based on its last successful sync and scheduled preview results.</summary>
public sealed record StaleSyncStatus(StaleSyncLevel Level, string Message, string ToolTip, double Progress)
{
    public static readonly StaleSyncStatus Fresh = new(StaleSyncLevel.Fresh, "", "", 0);
    public bool IsStale => Level is StaleSyncLevel.Stale or StaleSyncLevel.Overdue;
    public bool IsOverdue => Level == StaleSyncLevel.Overdue;
}

public static class StaleSyncEvaluator
{
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultOverdueAfter = TimeSpan.FromDays(7);

    public static StaleSyncStatus Evaluate(SyncProfile profile, IEnumerable<RunRecord> history, DateTimeOffset now)
    {
        var records = history.OrderByDescending(CompletedAt).ToList();
        var lastSuccessfulSync = records.FirstOrDefault(IsSuccessfulSync);
        var lastSyncAt = lastSuccessfulSync is null ? profile.CreatedAt : CompletedAt(lastSuccessfulSync);
        var latestPreview = records.FirstOrDefault(IsScheduledPreview);
        var previewHasChanges = latestPreview is not null
            && CompletedAt(latestPreview) > lastSyncAt
            && HasDelta(latestPreview);

        var verifiedAt = lastSyncAt;
        if (profile.Schedule.PreviewOnly && latestPreview is not null && !previewHasChanges)
        {
            verifiedAt = CompletedAt(latestPreview);
        }

        var elapsed = now - verifiedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var (staleAfter, overdueAfter, intervalBased) = Thresholds(profile);
        var timeStatus = FromElapsed(elapsed, staleAfter, overdueAfter, intervalBased);

        if (previewHasChanges)
        {
            var previewMessage = "Scheduled preview found pending changes";
            var previewDetails = DescribeDelta(latestPreview!);
            if (timeStatus.IsOverdue)
            {
                return timeStatus with
                {
                    Message = $"{previewMessage}; {timeStatus.Message}",
                    ToolTip = $"{previewDetails}. {timeStatus.ToolTip}",
                };
            }

            return new StaleSyncStatus(
                StaleSyncLevel.Stale,
                previewMessage,
                $"{previewDetails}. Run the profile to bring the destination up to date.",
                Math.Max(timeStatus.Progress, 1d / 3d));
        }

        return timeStatus;
    }

    private static (TimeSpan StaleAfter, TimeSpan OverdueAfter, bool IntervalBased) Thresholds(SyncProfile profile)
    {
        if (profile.Schedule.Enabled && profile.Schedule.Kind == ScheduleKind.Interval)
        {
            var interval = profile.Schedule.Interval;
            return (interval, Multiply(interval, 3), true);
        }

        return (DefaultStaleAfter, DefaultOverdueAfter, false);
    }

    private static StaleSyncStatus FromElapsed(TimeSpan elapsed, TimeSpan staleAfter, TimeSpan overdueAfter, bool intervalBased)
    {
        var progress = overdueAfter <= TimeSpan.Zero ? 1 : Math.Clamp(elapsed.TotalMilliseconds / overdueAfter.TotalMilliseconds, 0, 1);
        if (elapsed >= overdueAfter)
        {
            var reason = intervalBased
                ? $"no successful sync for {FormatAge(elapsed)} (3 scheduled intervals missed)"
                : $"no successful sync for {FormatAge(elapsed)}";
            return new StaleSyncStatus(StaleSyncLevel.Overdue, reason, $"STALE — {reason}.", progress);
        }

        if (elapsed >= staleAfter)
        {
            var reason = intervalBased
                ? $"no successful sync for {FormatAge(elapsed)} (a scheduled interval was missed)"
                : $"no successful sync for {FormatAge(elapsed)}";
            return new StaleSyncStatus(StaleSyncLevel.Stale, reason, $"STALE — {reason}.", progress);
        }

        return StaleSyncStatus.Fresh;
    }

    private static bool IsSuccessfulSync(RunRecord record) => !record.DryRun && record.Outcome == RunOutcome.Success;

    private static bool IsScheduledPreview(RunRecord record) =>
        record.DryRun && record.Trigger is RunTrigger.Scheduled or RunTrigger.TaskScheduler
        && record.Outcome is RunOutcome.Success or RunOutcome.Warning;

    private static bool HasDelta(RunRecord record) =>
        record.Summary is { } summary
        && (summary.Files.Copied > 0 || summary.Files.Extras > 0 || summary.Files.Mismatch > 0 || summary.Files.Failed > 0);

    private static DateTimeOffset CompletedAt(RunRecord record) => record.EndedAt > record.StartedAt ? record.EndedAt : record.StartedAt;

    private static TimeSpan Multiply(TimeSpan value, int multiplier)
    {
        if (value <= TimeSpan.Zero || value.Ticks > TimeSpan.MaxValue.Ticks / multiplier) return TimeSpan.MaxValue;
        return TimeSpan.FromTicks(value.Ticks * multiplier);
    }

    private static string DescribeDelta(RunRecord record)
    {
        var summary = record.Summary!;
        var parts = new List<string>();
        if (summary.Files.Copied > 0) parts.Add(FormatFiles(summary.Files.Copied, "to copy"));
        if (summary.Files.Extras > 0) parts.Add(FormatFiles(summary.Files.Extras, "extra"));
        if (summary.Files.Mismatch > 0) parts.Add(FormatFiles(summary.Files.Mismatch, "mismatched"));
        if (summary.Files.Failed > 0) parts.Add(FormatFiles(summary.Files.Failed, "failed"));
        return $"Scheduled preview found {string.Join(", ", parts)}";
    }

    private static string FormatFiles(long count, string label) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} file{(count == 1 ? "" : "s")} {label}";

    private static string FormatAge(TimeSpan elapsed) =>
        elapsed.TotalDays >= 1
            ? $"{Math.Floor(elapsed.TotalDays).ToString("N0", CultureInfo.CurrentCulture)} day{(elapsed.TotalDays < 2 ? "" : "s")}"
            : elapsed.TotalHours >= 1
                ? $"{Math.Floor(elapsed.TotalHours).ToString("N0", CultureInfo.CurrentCulture)} hour{(elapsed.TotalHours < 2 ? "" : "s")}"
                : $"{Math.Max(1, Math.Floor(elapsed.TotalMinutes)).ToString("N0", CultureInfo.CurrentCulture)} minutes";
}
