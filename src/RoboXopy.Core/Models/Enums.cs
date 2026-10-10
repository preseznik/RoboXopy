namespace RoboXopy.Core.Models;

public enum CopyMode
{
    /// <summary>Copy new and changed files; never delete anything.</summary>
    Copy,
    /// <summary>/MIR – destination becomes an exact copy of the source.</summary>
    Mirror,
    /// <summary>/MOV – copy files, then delete them from the source.</summary>
    MoveFiles,
    /// <summary>/MOVE – copy files and folders, then delete them from the source.</summary>
    MoveAll,
}

public enum SubdirectoryMode
{
    /// <summary>Top-level folder only.</summary>
    None,
    /// <summary>/S – subfolders, excluding empty ones.</summary>
    NonEmpty,
    /// <summary>/E – all subfolders, including empty ones.</summary>
    All,
}

public enum RestartMode
{
    Off,
    /// <summary>/Z</summary>
    Restartable,
    /// <summary>/B</summary>
    Backup,
    /// <summary>/ZB</summary>
    RestartableWithBackupFallback,
}

public enum ArchiveMode
{
    Ignore,
    /// <summary>/A – only files with the archive attribute set.</summary>
    OnlyArchived,
    /// <summary>/M – only archived files, and reset the attribute afterwards.</summary>
    OnlyArchivedAndReset,
}

public enum ScheduleKind
{
    Interval,
    Daily,
    Weekly,
    AtLogon,
}

public enum IntervalUnit
{
    Minutes,
    Hours,
}

public enum ScheduleRunner
{
    /// <summary>The RoboXopy tray app runs the profile while it is open.</summary>
    App,
    /// <summary>A Windows Task Scheduler task runs the profile, even when RoboXopy is closed.</summary>
    TaskScheduler,
}

public enum RunTrigger
{
    Manual,
    Scheduled,
    TaskScheduler,
    CommandLine,
}

public enum RunOutcome
{
    Success,
    Warning,
    Failed,
    Cancelled,
    Error,
    Skipped,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}
