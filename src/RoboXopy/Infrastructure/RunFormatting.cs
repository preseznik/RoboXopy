using System.Globalization;
using RoboXopy.Core.Formatting;
using RoboXopy.Core.Models;

namespace RoboXopy.Infrastructure;

/// <summary>Human-readable text for runs, sizes and durations, shared by the UI, tray and CLI.</summary>
public static class RunFormatting
{
    public static string FormatBytes(long bytes) => ByteSize.Format(bytes);

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalSeconds < 1) return "<1s";
        if (duration.TotalMinutes < 1) return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalHours < 1) return $"{duration.Minutes}m {duration.Seconds:00}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
    }

    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    public static string OutcomeTitle(RunOutcome outcome, bool dryRun) => (outcome, dryRun) switch
    {
        (RunOutcome.Success, true) => "Preview completed",
        (RunOutcome.Success, false) => "Completed",
        (RunOutcome.Warning, true) => "Preview completed with warnings",
        (RunOutcome.Warning, false) => "Completed with warnings",
        (RunOutcome.Failed, _) => "Failed",
        (RunOutcome.Cancelled, _) => "Cancelled",
        (RunOutcome.Error, _) => "Could not run",
        (RunOutcome.Skipped, _) => "Skipped",
        _ => outcome.ToString(),
    };

    public static string TriggerText(RunTrigger trigger) => trigger switch
    {
        RunTrigger.Manual => "Manual",
        RunTrigger.Scheduled => "Scheduled",
        RunTrigger.TaskScheduler => "Task Scheduler",
        RunTrigger.CommandLine => "Command line",
        _ => trigger.ToString(),
    };

    /// <summary>e.g. "12 copied · 340 unchanged · 1 failed · 2.3 MB".</summary>
    public static string SummaryText(RobocopySummary? summary, bool dryRun = false, bool purgesExtras = false)
    {
        if (summary is null) return "";
        var f = summary.Files;
        var parts = new List<string>
        {
            dryRun ? $"{f.Copied:N0} to copy" : $"{f.Copied:N0} copied",
            $"{f.Skipped:N0} unchanged",
        };
        if (f.Extras > 0)
        {
            parts.Add(!purgesExtras ? $"{f.Extras:N0} extra" : dryRun ? $"{f.Extras:N0} to remove" : $"{f.Extras:N0} removed");
        }
        if (f.Mismatch > 0) parts.Add($"{f.Mismatch:N0} mismatched");
        if (f.Failed > 0) parts.Add($"{f.Failed:N0} failed");
        if (summary.Bytes.Copied > 0) parts.Add(FormatBytes(summary.Bytes.Copied));
        return string.Join(" · ", parts);
    }

    public static bool ShouldNotify(RunRecord record, AppSettings settings)
    {
        if (!settings.ShowNotifications || record.Outcome is RunOutcome.Skipped) return false;
        if (settings.NotifyOnlyOnProblems) return record.Outcome is RunOutcome.Warning or RunOutcome.Failed or RunOutcome.Error;
        return true;
    }

    public static (string Title, string Message) Notification(RunRecord record)
    {
        var title = $"{record.ProfileName} – {OutcomeTitle(record.Outcome, record.DryRun).ToLowerInvariant()}";
        string message;
        if (record.Outcome is RunOutcome.Success or RunOutcome.Warning && record.Summary is { } summary)
        {
            var copied = summary.Files.Copied;
            message = copied == 0 && summary.Files.Extras == 0 && summary.Files.Failed == 0
                ? "Everything is already up to date."
                : SummaryText(summary, record.DryRun, record.PurgesExtras);
            message += $" ({FormatDuration(record.Duration)})";
            if (record.Outcome == RunOutcome.Warning && !string.IsNullOrEmpty(record.Message)) message = record.Message + "\n" + message;
        }
        else
        {
            message = record.Message ?? "";
        }

        return (title, message);
    }
}
