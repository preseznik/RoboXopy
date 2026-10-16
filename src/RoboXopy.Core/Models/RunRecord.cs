using System.Text.Json.Serialization;

namespace RoboXopy.Core.Models;

/// <summary>Totals parsed from the summary table robocopy prints at the end of a job.</summary>
public sealed record RobocopySummary
{
    public SummaryRow Dirs { get; init; } = new();
    public SummaryRow Files { get; init; } = new();
    public SummaryRow Bytes { get; init; } = new();
}

public sealed record SummaryRow
{
    public long Total { get; init; }
    public long Copied { get; init; }
    public long Skipped { get; init; }
    public long Mismatch { get; init; }
    public long Failed { get; init; }
    public long Extras { get; init; }
}

public enum VerificationStatus
{
    Passed,
    DifferencesFound,
    Failed,
}

/// <summary>Outcome of optional post-copy metadata verification.</summary>
public sealed record VerificationResult
{
    public VerificationStatus Status { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public RobocopySummary? Summary { get; init; }
}

/// <summary>One execution of a profile, persisted to the history store.</summary>
public sealed class RunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public RunTrigger Trigger { get; set; }
    public bool DryRun { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int ExitCode { get; set; }
    public RunOutcome Outcome { get; set; }
    public string? Message { get; set; }
    public string? LogFile { get; set; }
    public string CommandLine { get; set; } = "";
    public RobocopySummary? Summary { get; set; }

    /// <summary>True when the run deletes extra destination items (/MIR or /PURGE), so "extras" means removed.</summary>
    public bool PurgesExtras { get; set; }

    /// <summary>True when the run encountered an access-denied error or unreadable source folder.</summary>
    public bool HasPermissionIssue { get; set; }
    public VerificationResult? Verification { get; set; }
    public DestinationHealthSnapshot? DestinationHealth { get; set; }

    /// <summary>Unusual aspects of this run compared with earlier runs; null when there is nothing to report.</summary>
    public List<RunFinding>? Findings { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => EndedAt - StartedAt;

    [JsonIgnore]
    public bool HasWarnings => Findings?.Any(f => f.Severity == FindingSeverity.Warning) == true;
}
