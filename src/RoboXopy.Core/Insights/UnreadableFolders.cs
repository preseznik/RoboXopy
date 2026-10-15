using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Insights;

public enum FolderAccess
{
    Readable,
    Unreadable,
    /// <summary>A junction or symbolic link that couldn't be followed.</summary>
    UnreadableLink,
    /// <summary>The folder no longer exists.</summary>
    Missing,
}

/// <summary>
/// Robocopy counts a source folder it can't open as a failed directory but prints no error for it –
/// the log only shows the folder with zero files. This finds those folders again so the run can say which
/// ones were skipped and why.
/// </summary>
public static class UnreadableFolders
{
    /// <summary>At most this many folders are named in the finding.</summary>
    public const int MaxNamed = 5;

    /// <summary>Checking folders stops after this long so a slow share can't hold up the run.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(10);

    /// <param name="arguments">
    /// The run's robocopy arguments. Multithreaded runs and /NDL list no folders, so the source tree is then
    /// walked the way robocopy would to find them.
    /// </param>
    public static RunFinding? Analyze(
        RunRecord record,
        IEnumerable<OutputLine> lines,
        bool redirectionGuard,
        IReadOnlyList<string>? arguments = null,
        Func<string, FolderAccess>? probe = null,
        TimeSpan? budget = null)
    {
        var failed = record.Summary?.Dirs.Failed ?? 0;
        if (failed <= 0) return null;

        probe ??= Probe;
        var limit = budget ?? DefaultBudget;
        var stopwatch = Stopwatch.StartNew();
        bool OutOfTime() => stopwatch.Elapsed > limit;
        var found = new List<(string Path, bool Link)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawError = false;
        var listsFolders = false;

        foreach (var line in lines)
        {
            if (found.Count >= failed || OutOfTime()) break;
            if (line.Kind == OutputLineKind.Error) sawError = true;
            if (line.Kind == OutputLineKind.Directory) listsFolders = true;
            if (line.Kind != OutputLineKind.Directory || line.IsExtra || line.Count != 0) continue;
            if (line.Name is not { Length: > 0 } name || !Path.IsPathFullyQualified(name)) continue;

            var path = Path.TrimEndingDirectorySeparator(name);
            if (!seen.Add(path)) continue;

            var access = probe(path);
            if (access is FolderAccess.Unreadable or FolderAccess.UnreadableLink)
            {
                found.Add((path, access == FolderAccess.UnreadableLink));
            }
        }

        if (!listsFolders && !OutOfTime() && arguments is not null && SourceWalk.FromArguments(arguments) is { } walk)
        {
            found.AddRange(walk.FindUnreadable(failed, probe, OutOfTime));
        }

        // Robocopy explains the failures it can detect itself with ERROR lines.
        if (found.Count == 0 && sawError && !redirectionGuard) return null;

        return new RunFinding
        {
            Kind = RunFindingKind.UnreadableFolders,
            Severity = FindingSeverity.Warning,
            Message = Describe(failed, found, redirectionGuard),
            // Robocopy often omits the folder name for access-denied failures. In that case the
            // user should still be able to retry with an elevated token.
            MayRequireElevation = !redirectionGuard && (found.Count == 0 || found.Any(folder => !folder.Link)),
        };
    }

    private static string Describe(long failed, List<(string Path, bool Link)> found, bool redirectionGuard)
    {
        var count = failed.ToString("N0", CultureInfo.CurrentCulture);
        var folders = failed == 1 ? "folder" : "folders";

        if (found.Count == 0)
        {
            return redirectionGuard
                ? $"Robocopy couldn't open {count} source {folders}, so their contents were skipped. RoboXopy was started by an installer that " +
                  "blocks following junctions and symbolic links – exit RoboXopy completely (including the tray icon) and start it again."
                : $"Robocopy couldn't open {count} source {folders}, so their contents were skipped. Check the log for folders listed with 0 files " +
                  "– usually folders your account can't open, or junctions and symbolic links whose target is missing.";
        }

        var named = string.Join(", ", found.Take(MaxNamed).Select(f => f.Path));
        var more = failed - Math.Min(found.Count, MaxNamed);
        var list = more > 0 ? $"{named} and {more.ToString("N0", CultureInfo.CurrentCulture)} more" : named;
        var opening = $"Robocopy couldn't open {count} source {folders}, so their contents were skipped: {list}.";

        if (redirectionGuard && found.Any(f => f.Link))
        {
            return opening + " These are junctions or symbolic links, which RoboXopy can't follow because it was started by an installer that " +
                   "blocks them – exit RoboXopy completely (including the tray icon) and start it again.";
        }

        if (found.Any(f => f.Link))
        {
            return opening + " Some are junctions or symbolic links – check that their targets still exist, or exclude them in the profile's filters.";
        }

        return opening + " Your account can't open them – fix their permissions or exclude them in the profile's filters.";
    }

    /// <summary>Tries to list a folder's first entry.</summary>
    public static FolderAccess Probe(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return FolderAccess.Readable;
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return Classify(path, ex);
        }
    }

    private static bool IsAccessFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static FolderAccess Classify(string path, Exception failure)
    {
        bool isLink;
        try
        {
            isLink = File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            isLink = false;
        }

        // A junction whose target is gone still exists itself.
        if (failure is DirectoryNotFoundException) return isLink ? FolderAccess.UnreadableLink : FolderAccess.Missing;
        return isLink ? FolderAccess.UnreadableLink : FolderAccess.Unreadable;
    }

    /// <summary>The part of the source tree robocopy visits, per its /S, /E, /MIR, /LEV, /XD, /XJ and /XJD switches.</summary>
    internal sealed record SourceWalk(string Root, int MaxLevel, bool SkipLinks, IReadOnlyList<string> ExcludeDirs)
    {
        public static SourceWalk? FromArguments(IReadOnlyList<string> arguments)
        {
            if (arguments.Count < 2 || !Path.IsPathFullyQualified(arguments[0])) return null;

            var subdirectories = false;
            var skipLinks = false;
            var maxLevel = int.MaxValue;
            var excludeDirs = new List<string>();
            var inExcludeDirs = false;

            foreach (var argument in arguments.Skip(2))
            {
                if (!argument.StartsWith('/'))
                {
                    if (inExcludeDirs) excludeDirs.Add(argument);
                    continue;
                }

                inExcludeDirs = false;
                var name = argument.ToUpperInvariant();
                switch (name)
                {
                    case "/S" or "/E" or "/MIR":
                        subdirectories = true;
                        break;
                    case "/XJ" or "/XJD":
                        skipLinks = true;
                        break;
                    case "/XD":
                        inExcludeDirs = true;
                        break;
                    default:
                        if (name.StartsWith("/LEV:", StringComparison.Ordinal)
                            && int.TryParse(name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var levels)
                            && levels > 0)
                        {
                            maxLevel = levels;
                        }
                        break;
                }
            }

            return new SourceWalk(Path.TrimEndingDirectorySeparator(arguments[0]), subdirectories ? maxLevel : 1, skipLinks, excludeDirs);
        }

        // Backslash is the escape character in simple expressions.
        private bool IsExcluded(DirectoryInfo dir) => ExcludeDirs.Any(pattern =>
            pattern.Contains('\\')
                ? FileSystemName.MatchesSimpleExpression(Path.TrimEndingDirectorySeparator(pattern).Replace(@"\", @"\\"), dir.FullName)
                : FileSystemName.MatchesSimpleExpression(pattern, dir.Name));

        /// <summary>Walks breadth-first, like robocopy, until <paramref name="limit"/> folders are found or time runs out.</summary>
        public List<(string Path, bool Link)> FindUnreadable(long limit, Func<string, FolderAccess> probe, Func<bool> outOfTime)
        {
            var found = new List<(string Path, bool Link)>();
            var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
            var queue = new Queue<(DirectoryInfo Dir, int Level)>();
            queue.Enqueue((new DirectoryInfo(Root), 1));

            while (queue.Count > 0 && found.Count < limit && !outOfTime())
            {
                var (dir, level) = queue.Dequeue();
                List<DirectoryInfo> children;
                try
                {
                    children = dir.EnumerateDirectories("*", options).ToList();
                }
                catch (Exception ex) when (IsAccessFailure(ex))
                {
                    if (Classify(dir.FullName, ex) is var access and (FolderAccess.Unreadable or FolderAccess.UnreadableLink))
                    {
                        found.Add((dir.FullName, access == FolderAccess.UnreadableLink));
                    }
                    continue;
                }

                if (level >= MaxLevel) continue;

                foreach (var child in children)
                {
                    if (found.Count >= limit) break;
                    if (IsExcluded(child)) continue;

                    if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        if (SkipLinks) continue;

                        // Followed links are only checked, not walked, so a link back up the tree can't loop.
                        if (probe(child.FullName) is FolderAccess.Unreadable or FolderAccess.UnreadableLink)
                        {
                            found.Add((child.FullName, true));
                        }
                        continue;
                    }

                    queue.Enqueue((child, level + 1));
                }
            }

            return found;
        }
    }
}
