namespace RoboXopy.Core.Models;

public enum PreflightSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record PreflightFinding(PreflightSeverity Severity, string Message);

/// <summary>Fast, non-recursive destination checks used immediately before an interactive operation starts.</summary>
public sealed record ProfilePreflight(IReadOnlyList<PreflightFinding> Findings, long? DestinationFreeBytes)
{
    public bool HasErrors => Findings.Any(finding => finding.Severity == PreflightSeverity.Error);
    public bool HasWarnings => Findings.Any(finding => finding.Severity == PreflightSeverity.Warning);

    public static ProfilePreflight Check(SyncProfile profile)
    {
        var findings = new List<PreflightFinding>();
        var source = FolderSafety.NormalizeFolder(profile.Source);
        var destination = FolderSafety.NormalizeFolder(profile.Destination);
        if (source is null || destination is null) return new ProfilePreflight(findings, null);

        if (!Directory.Exists(source))
        {
            findings.Add(new(PreflightSeverity.Error, $"The source folder “{source}” is unavailable."));
            return new ProfilePreflight(findings, null);
        }

        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new(PreflightSeverity.Error, "Source and destination resolve to the same folder."));
            return new ProfilePreflight(findings, null);
        }

        var root = Path.GetPathRoot(destination);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            findings.Add(new(PreflightSeverity.Error, $"The destination volume for “{destination}” is unavailable."));
            return new ProfilePreflight(findings, null);
        }

        if (profile.Options.Mode == CopyMode.Mirror || profile.Options.Purge)
        {
            findings.Add(new(PreflightSeverity.Warning, "This operation can delete destination-only files."));
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                findings.Add(new(PreflightSeverity.Error, $"The destination volume “{root}” is not ready."));
                return new ProfilePreflight(findings, null);
            }

            var free = drive.AvailableFreeSpace;
            if (free < 1L << 30)
            {
                findings.Add(new(PreflightSeverity.Warning, $"The destination has less than 1 GB free ({FormatBytes(free)} available)."));
            }
            else
            {
                findings.Add(new(PreflightSeverity.Info, $"{FormatBytes(free)} available on the destination volume."));
            }

            return new ProfilePreflight(findings, free);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            findings.Add(new(PreflightSeverity.Warning, $"RoboXopy couldn't inspect free space on the destination volume: {ex.Message}"));
            return new ProfilePreflight(findings, null);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
