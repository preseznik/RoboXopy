using System.Globalization;

namespace RoboXopy.Core.Models;

/// <summary>Interprets the actionable delta in a scheduled dry-run.</summary>
public static class ScheduledPreviewAnalysis
{
    public static bool HasDelta(RunRecord record) =>
        record.DryRun
        && record.Summary is { } summary
        && (summary.Files.Copied > 0 || summary.Files.Extras > 0 || summary.Files.Mismatch > 0 || summary.Files.Failed > 0);

    public static string Summary(RunRecord record)
    {
        if (record.Summary is not { } summary) return record.Message ?? "The preview completed.";

        var parts = new List<string>();
        if (summary.Files.Copied > 0) parts.Add($"{summary.Files.Copied.ToString("N0", CultureInfo.CurrentCulture)} to copy");
        if (summary.Files.Extras > 0) parts.Add($"{summary.Files.Extras.ToString("N0", CultureInfo.CurrentCulture)} extra");
        if (summary.Files.Mismatch > 0) parts.Add($"{summary.Files.Mismatch.ToString("N0", CultureInfo.CurrentCulture)} mismatched");
        if (summary.Files.Failed > 0) parts.Add($"{summary.Files.Failed.ToString("N0", CultureInfo.CurrentCulture)} failed");
        return parts.Count == 0 ? "No pending changes." : string.Join(" · ", parts);
    }
}
