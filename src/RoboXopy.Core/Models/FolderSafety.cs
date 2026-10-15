namespace RoboXopy.Core.Models;

/// <summary>Normalizes folders and identifies destinations that overlap folders previously used as sources.</summary>
public static class FolderSafety
{
    public static string? NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            var root = Path.GetPathRoot(fullPath);
            return fullPath.Length > (root?.Length ?? 0) ? fullPath.TrimEnd('\\', '/') : fullPath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool DestinationOverlapsSourceHistory(string? destination, IEnumerable<string> sourceHistory)
    {
        var normalizedDestination = NormalizeFolder(destination);
        if (normalizedDestination is null) return false;

        return sourceHistory
            .Select(NormalizeFolder)
            .Where(source => source is not null)
            .Any(source => Overlaps(normalizedDestination, source!));
    }

    public static bool HasConfirmedHistoricalSourceDestination(SyncProfile profile)
    {
        var destination = NormalizeFolder(profile.Destination);
        return destination is not null
            && string.Equals(destination, profile.ConfirmedHistoricalSourceDestination, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Overlaps(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase)
        || IsInside(first, second)
        || IsInside(second, first);

    private static bool IsInside(string candidate, string parent)
    {
        var prefix = parent.EndsWith('\\') ? parent : parent + "\\";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
