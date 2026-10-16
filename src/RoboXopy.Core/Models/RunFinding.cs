namespace RoboXopy.Core.Models;

public enum RunFindingKind
{
    /// <summary>A mirror/purge removes far more from the destination than usual.</summary>
    LargeRemoval,
    /// <summary>The source has far fewer files than usual (or none at all).</summary>
    SourceShrank,
    /// <summary>Far more data is copied than usual.</summary>
    LargeChange,
    /// <summary>Robocopy couldn't open some source folders and skipped their contents without reporting an error.</summary>
    UnreadableFolders,
    /// <summary>Optional post-copy metadata verification found remaining differences or could not run.</summary>
    Verification,
}

public enum FindingSeverity
{
    Info,
    Warning,
}

/// <summary>Something unusual about a run, mostly compared with the profile's own history.</summary>
public sealed record RunFinding
{
    public RunFindingKind Kind { get; init; }
    public FindingSeverity Severity { get; init; }
    public string Message { get; init; } = "";

    /// <summary>True when the finding indicates access that an elevated process may have.</summary>
    public bool MayRequireElevation { get; init; }
}
