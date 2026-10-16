namespace RoboXopy.Core.Models;

/// <summary>Builds a temporary, safe reverse-copy profile for restoring an existing profile's backup.</summary>
public static class RestoreProfileFactory
{
    public static SyncProfile Create(SyncProfile original, string backupSource, string restoreDestination) => new()
    {
        // Keep the original id so the restore's log and history remain with the profile it restores.
        Id = original.Id,
        Name = original.Name,
        Source = backupSource.Trim(),
        Destination = restoreDestination.Trim(),
        SortOrder = original.SortOrder,
        CreatedAt = original.CreatedAt,
        Options = new RobocopyOptions
        {
            // Restores must never mirror, purge, move, or replay arbitrary raw switches from the backup profile.
            Mode = CopyMode.Copy,
            Subdirectories = SubdirectoryMode.All,
            Purge = false,
            Multithreaded = true,
            ThreadCount = 8,
            ExcludeJunctions = true,
            Retries = 3,
            WaitSeconds = 5,
        },
        Schedule = new ScheduleSettings(),
    };
}
