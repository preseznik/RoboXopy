namespace RoboXopy.Core.Models;

public enum RunFindingKind
{
    /// <summary>A mirror/purge removes far more from the destination than usual.</summary>
    LargeRemoval,
    /// <summary>The source has far fewer files than usual (or none at all).</summary>
    SourceShrank,
    /// <summary>Far more data is copied than usual.</summary>
    LargeChange,
}

public enum FindingSeverity
{
    Info,
    Warning,
}

/// <summary>Something unusual about a run compared with the profile's own history.</summary>
public sealed record RunFinding
{
    public RunFindingKind Kind { get; init; }
    public FindingSeverity Severity { get; init; }
    public string Message { get; init; } = "";
}
