using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ProfileIssue(IssueSeverity Severity, string Message);

/// <summary>Checks a profile for problems before running it and for display in the editor.</summary>
public static class ProfileValidator
{
    public static IReadOnlyList<ProfileIssue> Validate(SyncProfile profile, bool checkFileSystem = true)
    {
        var issues = new List<ProfileIssue>();
        var source = CommandLine.NormalizePath(profile.Source);
        var destination = CommandLine.NormalizePath(profile.Destination);
        var o = profile.Options;

        if (source.Length == 0)
        {
            issues.Add(new(IssueSeverity.Error, "Choose a source folder."));
        }
        else if (checkFileSystem && !Directory.Exists(source))
        {
            issues.Add(new(IssueSeverity.Error, $"Source folder \"{source}\" was not found."));
        }

        if (destination.Length == 0)
        {
            issues.Add(new(IssueSeverity.Error, "Choose a destination folder."));
        }

        if (source.Length > 0 && destination.Length > 0)
        {
            var s = SafeFullPath(source);
            var d = SafeFullPath(destination);
            if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(IssueSeverity.Error, "Source and destination are the same folder."));
            }
            else if (o.Subdirectories != SubdirectoryMode.None || o.Mode == CopyMode.Mirror)
            {
                if (IsInside(d, s))
                {
                    issues.Add(new(IssueSeverity.Error, "Destination is inside the source folder, which would copy the backup into itself."));
                }
                else if (o.Mode == CopyMode.Mirror && IsInside(s, d))
                {
                    issues.Add(new(IssueSeverity.Error, "Source is inside the destination folder; mirroring would delete everything else in it."));
                }
            }
        }

        if (o.Mode == CopyMode.Mirror)
        {
            issues.Add(new(IssueSeverity.Warning, "Mirror deletes files and folders in the destination that don't exist in the source."));
        }
        else if (o.Mode == CopyMode.Copy && o.Purge)
        {
            issues.Add(new(IssueSeverity.Warning, "Purge deletes files and folders in the destination that don't exist in the source."));
        }
        else if (o.Mode is CopyMode.MoveFiles or CopyMode.MoveAll)
        {
            issues.Add(new(IssueSeverity.Warning, "Move deletes files from the source after they are copied."));
        }

        if (o.Multithreaded && o.InterPacketGapMs is > 0)
        {
            issues.Add(new(IssueSeverity.Info, "Multithreading is turned off while bandwidth throttling (inter-packet gap) is set."));
        }

        if (RobocopyCommandBuilder.FileCopyFlags(o).Length == 0)
        {
            issues.Add(new(IssueSeverity.Info, "No file properties are selected, so robocopy's default (data, attributes, timestamps) is used."));
        }

        if (o.RestartMode is RestartMode.Backup or RestartMode.RestartableWithBackupFallback || o.CopyFileAuditing)
        {
            issues.Add(new(IssueSeverity.Info, "Backup mode and auditing info require running RoboXopy as administrator."));
        }

        return issues;
    }

    public static bool CanRun(SyncProfile profile) =>
        Validate(profile).All(i => i.Severity != IssueSeverity.Error);

    internal static bool IsInside(string candidate, string parent)
    {
        var p = parent.EndsWith('\\') ? parent : parent + "\\";
        return candidate.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return CommandLine.NormalizePath(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
