using System.IO.Enumeration;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

public enum RuleTestOutcome
{
    Included,
    Excluded,
    CannotEvaluate,
}

/// <summary>Explains how an existing source file or folder is affected by a profile's local filters.</summary>
public sealed record RuleTestResult(RuleTestOutcome Outcome, string Summary, IReadOnlyList<string> Reasons)
{
    public bool IsIncluded => Outcome == RuleTestOutcome.Included;
}

public static class ProfileRuleTester
{
    public static RuleTestResult Test(SyncProfile profile, string path, DateTime? now = null)
    {
        var source = Normalize(profile.Source);
        var candidate = Normalize(path);
        if (source is null || candidate is null)
        {
            return CannotEvaluate("Enter a valid source path and file or folder path.");
        }

        if (!IsInsideOrSame(candidate, source))
        {
            return CannotEvaluate("The selected path is outside this profile's source folder.");
        }

        var isFile = File.Exists(candidate);
        var isDirectory = Directory.Exists(candidate);
        if (!isFile && !isDirectory)
        {
            return CannotEvaluate("The path does not exist, so its age, size, and attributes cannot be evaluated.");
        }

        var relative = Path.GetRelativePath(source, candidate);
        if (relative == "." && isDirectory)
        {
            return new RuleTestResult(RuleTestOutcome.Included, "Included", ["This is the source root folder."]);
        }

        var reasons = new List<string>();
        var options = profile.Options;
        if (options.Subdirectories == SubdirectoryMode.None && relative.Contains(Path.DirectorySeparatorChar))
        {
            reasons.Add("Excluded: this profile only includes the top-level source folder.");
            return Excluded(reasons);
        }

        if (MatchesExcludedDirectory(source, candidate, isDirectory, options.ExcludeDirectories, out var directoryPattern))
        {
            reasons.Add($"Excluded by directory rule “{directoryPattern}”.");
            return Excluded(reasons);
        }

        if (isDirectory)
        {
            reasons.Add("Included: no directory exclusion matches.");
            return new RuleTestResult(RuleTestOutcome.Included, "Included", reasons);
        }

        var fileName = Path.GetFileName(candidate);
        if (options.IncludeFiles.Count > 0 && !options.IncludeFiles.Any(pattern => MatchesFilePattern(fileName, pattern)))
        {
            reasons.Add("Excluded: it does not match any included file pattern.");
            return Excluded(reasons);
        }

        if (options.IncludeFiles.Count > 0)
        {
            var pattern = options.IncludeFiles.First(pattern => MatchesFilePattern(fileName, pattern));
            reasons.Add($"Included by file pattern “{pattern}”.");
        }

        if (options.ExcludeFiles.FirstOrDefault(pattern => MatchesFilePattern(fileName, pattern)) is { } excludedPattern)
        {
            reasons.Add($"Excluded by file rule “{excludedPattern}”.");
            return Excluded(reasons);
        }

        var info = new FileInfo(candidate);
        var today = (now ?? DateTime.Now).Date;
        if (options.MaxAgeDays is > 0 && info.LastWriteTime.Date < today.AddDays(-options.MaxAgeDays.Value))
        {
            reasons.Add($"Excluded: modified before the maximum age of {options.MaxAgeDays:N0} days.");
            return Excluded(reasons);
        }

        if (options.MinAgeDays is > 0 && info.LastWriteTime.Date > today.AddDays(-options.MinAgeDays.Value))
        {
            reasons.Add($"Excluded: modified more recently than the minimum age of {options.MinAgeDays:N0} days.");
            return Excluded(reasons);
        }

        if (options.MaxFileSizeBytes is > 0 && info.Length > options.MaxFileSizeBytes.Value)
        {
            reasons.Add($"Excluded: size is above the maximum of {options.MaxFileSizeBytes:N0} bytes.");
            return Excluded(reasons);
        }

        if (options.MinFileSizeBytes is > 0 && info.Length < options.MinFileSizeBytes.Value)
        {
            reasons.Add($"Excluded: size is below the minimum of {options.MinFileSizeBytes:N0} bytes.");
            return Excluded(reasons);
        }

        if (options.Archive is ArchiveMode.OnlyArchived or ArchiveMode.OnlyArchivedAndReset
            && !info.Attributes.HasFlag(FileAttributes.Archive))
        {
            reasons.Add("Excluded: the archive attribute is not set.");
            return Excluded(reasons);
        }

        reasons.Add($"Included: {info.Length:N0} bytes, modified {info.LastWriteTime:g}.");
        if (options.ExcludeOlder || options.ExcludeNewer || options.ExcludeChanged || options.ExcludeExtra
            || options.ExcludeLonely || options.IncludeSame || options.IncludeTweaked)
        {
            reasons.Add("Change-type filters require comparing the destination and are not evaluated here.");
        }

        return new RuleTestResult(RuleTestOutcome.Included, "Included", reasons);
    }

    private static RuleTestResult Excluded(IReadOnlyList<string> reasons) =>
        new(RuleTestOutcome.Excluded, "Excluded", reasons);

    private static RuleTestResult CannotEvaluate(string reason) =>
        new(RuleTestOutcome.CannotEvaluate, "Cannot evaluate", [reason]);

    private static bool MatchesExcludedDirectory(string source, string candidate, bool candidateIsDirectory, IEnumerable<string> patterns, out string? matchedPattern)
    {
        var directory = candidateIsDirectory ? candidate : Path.GetDirectoryName(candidate);
        while (!string.IsNullOrEmpty(directory) && IsInsideOrSame(directory, source))
        {
            foreach (var pattern in patterns)
            {
                var trimmed = pattern.Trim().Trim('"');
                if (trimmed.Length == 0) continue;
                var match = trimmed.Contains('\\') || trimmed.Contains('/')
                    ? string.Equals(Normalize(trimmed), directory, StringComparison.OrdinalIgnoreCase)
                    : FileSystemName.MatchesSimpleExpression(trimmed, Path.GetFileName(directory), ignoreCase: true);
                if (match)
                {
                    matchedPattern = pattern;
                    return true;
                }
            }

            if (string.Equals(directory, source, StringComparison.OrdinalIgnoreCase)) break;
            directory = Path.GetDirectoryName(directory);
        }

        matchedPattern = null;
        return false;
    }

    private static bool MatchesFilePattern(string fileName, string pattern) =>
        !string.IsNullOrWhiteSpace(pattern)
        && FileSystemName.MatchesSimpleExpression(pattern.Trim().Trim('"'), fileName, ignoreCase: true);

    private static string? Normalize(string path)
    {
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

    private static bool IsInsideOrSame(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root.EndsWith('\\') ? root : root + "\\", StringComparison.OrdinalIgnoreCase);
}
