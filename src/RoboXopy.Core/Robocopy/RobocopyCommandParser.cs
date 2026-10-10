using System.Globalization;
using System.Text.RegularExpressions;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

public enum ImportItemKind
{
    /// <summary>Mapped onto a RoboXopy setting.</summary>
    Mapped,
    /// <summary>No matching setting; passed to robocopy unchanged via extra arguments.</summary>
    Extra,
    /// <summary>Not imported.</summary>
    Dropped,
}

/// <summary>How one argument of an imported robocopy command was handled.</summary>
public sealed record ImportItem(string Argument, string Description, ImportItemKind Kind);

public sealed record RobocopyImportResult(SyncProfile Profile, IReadOnlyList<ImportItem> Items, IReadOnlyList<ImportNote> Notes)
{
    public bool HasErrors => Notes.Any(n => n.Severity == IssueSeverity.Error);
}

/// <summary>Turns a robocopy command line into a <see cref="SyncProfile"/>, explaining how each argument was imported.</summary>
public static partial class RobocopyCommandParser
{
    public const string DefaultName = "Imported profile";

    /// <summary>Switches without a RoboXopy setting that are safe to pass through unchanged.</summary>
    private static readonly HashSet<string> PassThroughSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "/LEV", "/SECFIX", "/TIMFIX", "/XJD", "/XJF", "/IA", "/XA", "/A+", "/A-", "/CREATE", "/FAT", "/256", "/RH", "/PF",
        "/EFSRAW", "/NOOFFLOAD", "/SJ", "/SPARSE", "/NOCOPY", "/NODCOPY", "/MAXLAD", "/MINLAD", "/IM", "/TBD", "/X",
        "/IOMAXSIZE", "/IORATE", "/THRESHOLD", "/LFSM", "/NOCLONE", "/DEBUG",
    };

    public static RobocopyImportResult Parse(string commandLine, string? workingDirectory = null, string? batchPath = null)
    {
        var tokens = CommandLine.Split(commandLine).ToList();
        if (tokens.Count > 0 && BatchFileReader.IsRobocopy(tokens[0])) tokens.RemoveAt(0);

        var b = new Builder(workingDirectory);
        foreach (var token in tokens)
        {
            if (token.StartsWith('/') && token.Length > 1)
            {
                b.EndList();
                Switch(b, token);
            }
            else
            {
                b.Positional(token);
            }
        }

        return b.Finish(batchPath);
    }

    /// <summary>
    /// A friendly profile name from the batch file name ("robocopy_photos.bat" → "Photos"), falling back to the
    /// source folder's name.
    /// </summary>
    public static string SuggestName(string? batchPath, string? source)
    {
        var name = "";
        if (!string.IsNullOrWhiteSpace(batchPath))
        {
            var words = NameSeparatorRegex().Split(Path.GetFileNameWithoutExtension(batchPath))
                .Where(w => w.Length > 0 && !RobocopyWordRegex().IsMatch(w));
            name = string.Join(' ', words);
        }

        if (name.Length == 0 && !string.IsNullOrWhiteSpace(source) && !source.Contains('%'))
        {
            name = Path.GetFileName(source.Trim().TrimEnd('\\', '/'));
        }

        if (name.Length == 0) return DefaultName;
        return char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }

    [GeneratedRegex(@"[_\-\s.]+")]
    private static partial Regex NameSeparatorRegex();

    [GeneratedRegex(@"^robo(copy|coy|cpy|cp|cop)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RobocopyWordRegex();

    private static void Switch(Builder b, string token)
    {
        var colon = token.IndexOf(':');
        var name = (colon < 0 ? token : token[..colon]).ToUpperInvariant();
        var value = colon < 0 ? null : token[(colon + 1)..];
        var o = b.Options;

        switch (name)
        {
            case "/MIR":
                b.Mirror = true;
                b.Map(token, "Mirror: make the destination an exact copy of the source");
                break;
            case "/E":
                b.AllSubdirectories = true;
                b.Map(token, "Include all subfolders, including empty ones");
                break;
            case "/S":
                b.NonEmptySubdirectories = true;
                b.Map(token, "Include subfolders, except empty ones");
                break;
            case "/PURGE":
                b.Purge = true;
                b.Map(token, "Delete destination files and folders that no longer exist in the source");
                break;
            case "/MOV":
                b.MoveFiles = true;
                b.Map(token, "Move files (delete them from the source after copying)");
                break;
            case "/MOVE":
                b.MoveAll = true;
                b.Map(token, "Move files and folders (delete them from the source after copying)");
                break;
            case "/COPY":
                FileCopyFlags(b, token, value);
                break;
            case "/DCOPY":
                DirCopyFlags(b, token, value);
                break;
            case "/COPYALL":
                SetFileFlags(o, "DATSOU");
                b.Map(token, "Copy file data, attributes, timestamps, security, owner and auditing info");
                break;
            case "/SEC":
                SetFileFlags(o, "DATS");
                b.Map(token, "Copy file data, attributes, timestamps and security");
                break;
            case "/MT":
                var threads = 8;
                if (value is not null && !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out threads))
                {
                    b.Invalid(token);
                    break;
                }

                o.Multithreaded = true;
                o.ThreadCount = Math.Clamp(threads, 1, 128);
                if (o.ThreadCount != threads)
                {
                    b.Note(IssueSeverity.Warning, $"Robocopy supports 1 to 128 threads, so {token} was changed to {o.ThreadCount} threads.");
                }

                b.Map(token, $"Multithreaded copying with {o.ThreadCount} threads");
                break;
            case "/Z":
                b.Restartable = true;
                b.Map(token, "Restartable mode");
                break;
            case "/B":
                b.Backup = true;
                b.Map(token, "Backup mode");
                break;
            case "/ZB":
                b.Restartable = b.Backup = true;
                b.Map(token, "Restartable mode, falling back to backup mode");
                break;
            case "/J":
                o.Unbuffered = true;
                b.Map(token, "Unbuffered I/O, for large files");
                break;
            case "/COMPRESS":
                o.Compress = true;
                b.Map(token, "Compress data sent over the network");
                break;
            case "/XJ":
                o.ExcludeJunctions = true;
                b.Map(token, "Skip junction points");
                break;
            case "/SL":
                o.CopySymlinksAsLinks = true;
                b.Map(token, "Copy symbolic links as links");
                break;
            case "/FFT":
                o.FatFileTimes = true;
                b.Map(token, "Use FAT file times (2-second precision)");
                break;
            case "/DST":
                o.CompensateDst = true;
                b.Map(token, "Compensate for daylight saving time differences");
                break;
            case "/IPG":
                if (TryInt(value, out var gap) && gap > 0)
                {
                    o.InterPacketGapMs = gap;
                    b.Map(token, $"Wait {gap} ms between packets to limit bandwidth");
                }
                else b.Invalid(token);

                break;
            case "/XF":
                b.StartList(token, o.ExcludeFiles, "Exclude files");
                break;
            case "/XD":
                b.StartList(token, o.ExcludeDirectories, "Exclude folders");
                break;
            case "/IF":
                b.StartList(token, o.IncludeFiles, "Only include files");
                break;
            case "/XO":
                o.ExcludeOlder = true;
                b.Map(token, "Skip files that are older than the destination copy");
                break;
            case "/XN":
                o.ExcludeNewer = true;
                b.Map(token, "Skip files that are newer than the destination copy");
                break;
            case "/XC":
                o.ExcludeChanged = true;
                b.Map(token, "Skip changed files");
                break;
            case "/XX":
                o.ExcludeExtra = true;
                b.Map(token, "Keep extra files in the destination");
                break;
            case "/XL":
                o.ExcludeLonely = true;
                b.Map(token, "Skip files that don't exist in the destination");
                break;
            case "/IS":
                o.IncludeSame = true;
                b.Map(token, "Copy files even when they're the same");
                break;
            case "/IT":
                o.IncludeTweaked = true;
                b.Map(token, "Copy files whose attributes changed");
                break;
            case "/MAXAGE" or "/MINAGE":
                if (!TryInt(value, out var age)) b.Invalid(token);
                else if (age >= 1900) b.Extra(token, known: true);
                else
                {
                    if (name == "/MAXAGE") o.MaxAgeDays = age;
                    else o.MinAgeDays = age;
                    b.Map(token, name == "/MAXAGE" ? $"Skip files older than {Days(age)}" : $"Skip files newer than {Days(age)}");
                }

                break;
            case "/MAX" or "/MIN":
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)) b.Invalid(token);
                else
                {
                    if (name == "/MAX") o.MaxFileSizeBytes = size;
                    else o.MinFileSizeBytes = size;
                    b.Map(token, name == "/MAX" ? $"Skip files larger than {Size(size)}" : $"Skip files smaller than {Size(size)}");
                }

                break;
            case "/A":
                o.Archive = ArchiveMode.OnlyArchived;
                b.Map(token, "Only copy files with the archive attribute set");
                break;
            case "/M":
                o.Archive = ArchiveMode.OnlyArchivedAndReset;
                b.Map(token, "Only copy files with the archive attribute set, then clear it");
                break;
            case "/R":
                if (TryInt(value, out var retries))
                {
                    o.Retries = retries;
                    b.HasRetries = true;
                    b.Map(token, retries == 1 ? "Retry failed copies once" : $"Retry failed copies {retries:N0} times");
                }
                else b.Invalid(token);

                break;
            case "/W":
                if (TryInt(value, out var wait))
                {
                    o.WaitSeconds = wait;
                    b.HasWait = true;
                    b.Map(token, wait == 1 ? "Wait 1 second between retries" : $"Wait {wait:N0} seconds between retries");
                }
                else b.Invalid(token);

                break;
            case "/V":
                o.Verbose = true;
                b.Map(token, "Verbose output, including skipped files");
                break;
            case "/TS":
                o.Timestamps = true;
                b.Map(token, "Show file timestamps in the output");
                break;
            case "/FP":
                o.FullPaths = true;
                b.Map(token, "Show full paths in the output");
                break;
            case "/NFL":
                o.NoFileList = true;
                b.Map(token, "Don't list files in the output");
                break;
            case "/NDL":
                o.NoDirList = true;
                b.Map(token, "Don't list folders in the output");
                break;
            case "/BYTES":
                o.Bytes = true;
                b.Map(token, "Show sizes in bytes");
                break;
            case "/NP":
                o.NoProgress = true;
                b.Map(token, "Don't show per-file progress");
                break;
            case "/L":
                b.Drop(token, "Not imported. Use Preview to see what would change");
                b.Note(IssueSeverity.Warning,
                    "The batch file only lists what robocopy would do (/L). The imported profile copies for real — use Preview for a dry run.");
                break;
            case "/LOG" or "/LOG+" or "/UNILOG" or "/UNILOG+":
                b.Drop(token, "Not imported. RoboXopy keeps each run's output in History");
                b.Note(IssueSeverity.Info, "Log file switches weren't imported. RoboXopy keeps the output of every run in the profile's History.");
                break;
            case "/TEE":
                b.Drop(token, "Not needed. RoboXopy shows robocopy's output as it runs");
                break;
            case "/NJH" or "/NJS" or "/NC" or "/NS" or "/ETA" or "/UNICODE":
                b.Drop(token, "Not imported. RoboXopy reads this output to show progress and results");
                b.Note(IssueSeverity.Info,
                    "Switches that hide or change robocopy's job header, summary or file details weren't imported, because RoboXopy reads them to show progress and results.");
                break;
            case "/MON" or "/MOT":
                b.Drop(token, "Not imported. Use a schedule to repeat the sync");
                b.Note(IssueSeverity.Warning,
                    "/MON and /MOT keep robocopy running to watch for changes. RoboXopy runs robocopy once per sync instead — add a schedule to repeat it.");
                break;
            case "/JOB" or "/SAVE":
                b.Drop(token, "Not supported");
                b.Note(IssueSeverity.Warning,
                    "Robocopy job files (/JOB, /SAVE) aren't supported. Any settings stored in a job file weren't imported.");
                break;
            case "/QUIT" or "/NOSD" or "/NODD" or "/REG":
                b.Drop(token, "Not supported");
                break;
            default:
                b.Extra(token, PassThroughSwitches.Contains(name));
                break;
        }
    }

    private static void FileCopyFlags(Builder b, string token, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            b.Invalid(token);
            return;
        }

        var unknown = new string(value.ToUpperInvariant().Where(c => !"DATSOU".Contains(c)).Distinct().ToArray());
        SetFileFlags(b.Options, value.ToUpperInvariant());
        b.Map(token, "Copy file " + Describe(value, ("D", "data"), ("A", "attributes"), ("T", "timestamps"), ("S", "security"), ("O", "owner"), ("U", "auditing info")));
        if (unknown.Length > 0)
        {
            b.Note(IssueSeverity.Warning, $"{token}: RoboXopy doesn't support the copy flag{(unknown.Length > 1 ? "s" : "")} {unknown}, so {(unknown.Length > 1 ? "they were" : "it was")} ignored.");
        }
    }

    private static void DirCopyFlags(Builder b, string token, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            b.Invalid(token);
            return;
        }

        var flags = value.ToUpperInvariant();
        var o = b.Options;
        o.CopyDirData = flags.Contains('D');
        o.CopyDirAttributes = flags.Contains('A');
        o.CopyDirTimestamps = flags.Contains('T');
        b.Map(token, "Copy folder " + Describe(value, ("D", "data"), ("A", "attributes"), ("T", "timestamps")));

        var unknown = new string(flags.Where(c => !"DAT".Contains(c)).Distinct().ToArray());
        if (unknown.Length > 0)
        {
            b.Note(IssueSeverity.Warning, $"{token}: RoboXopy doesn't support the folder copy flag{(unknown.Length > 1 ? "s" : "")} {unknown}, so {(unknown.Length > 1 ? "they were" : "it was")} ignored.");
        }
    }

    private static void SetFileFlags(RobocopyOptions o, string flags)
    {
        o.CopyFileData = flags.Contains('D');
        o.CopyFileAttributes = flags.Contains('A');
        o.CopyFileTimestamps = flags.Contains('T');
        o.CopyFileSecurity = flags.Contains('S');
        o.CopyFileOwner = flags.Contains('O');
        o.CopyFileAuditing = flags.Contains('U');
    }

    private static string Describe(string flags, params (string Flag, string Text)[] names)
    {
        var parts = names.Where(n => flags.Contains(n.Flag, StringComparison.OrdinalIgnoreCase)).Select(n => n.Text).ToList();
        return parts.Count switch
        {
            0 => "(nothing)",
            1 => parts[0],
            _ => string.Join(", ", parts[..^1]) + " and " + parts[^1],
        };
    }

    private static bool TryInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static string Days(int days) => days == 1 ? "1 day" : $"{days:N0} days";

    private static string Size(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1 && value % 1024 == 0)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} bytes" : $"{value:N0} {units[unit]}";
    }

    private static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private sealed class Entry(string argument, string description, ImportItemKind kind)
    {
        public string Argument { get; set; } = argument;
        public string Description { get; set; } = description;
        public ImportItemKind Kind { get; set; } = kind;
    }

    private sealed class Builder(string? workingDirectory)
    {
        private readonly SyncProfile _profile = CreateProfile();
        private readonly List<Entry> _items = [];
        private readonly List<ImportNote> _notes = [];
        private readonly List<string> _extra = [];
        private readonly List<string> _knownExtra = [];
        private readonly List<string> _unknownExtra = [];
        private readonly List<string> _patterns = [];
        private Entry? _patternItem;
        private string? _source;
        private string? _destination;

        private (string Switch, ICollection<string> Target, string Label, Entry Item, List<string> Values)? _list;

        public RobocopyOptions Options => _profile.Options;
        public bool Mirror, AllSubdirectories, NonEmptySubdirectories, Purge, MoveFiles, MoveAll, Restartable, Backup, HasRetries, HasWait;

        /// <summary>A profile with robocopy's own defaults, so absent switches mean what they mean to robocopy.</summary>
        private static SyncProfile CreateProfile()
        {
            var profile = new SyncProfile();
            var o = profile.Options;
            o.Mode = CopyMode.Copy;
            o.Subdirectories = SubdirectoryMode.None;
            o.Multithreaded = false;
            o.ExcludeJunctions = false;
            o.CopyDirTimestamps = false;
            return profile;
        }

        public void Map(string argument, string description) => _items.Add(new(argument, description, ImportItemKind.Mapped));

        public void Drop(string argument, string description) => _items.Add(new(argument, description, ImportItemKind.Dropped));

        public void Note(IssueSeverity severity, string message)
        {
            var note = new ImportNote(severity, message);
            if (!_notes.Contains(note)) _notes.Add(note);
        }

        public void Invalid(string argument)
        {
            Drop(argument, "Not imported: the value isn't valid");
            Note(IssueSeverity.Warning, $"{argument} doesn't have a valid value, so it wasn't imported.");
        }

        public void Extra(string argument, bool known)
        {
            _extra.Add(Environment.ExpandEnvironmentVariables(argument));
            (known ? _knownExtra : _unknownExtra).Add(argument);
            _items.Add(new(argument, known ? "Passed to robocopy as is" : "Unrecognized — passed to robocopy as is", ImportItemKind.Extra));
        }

        public void StartList(string argument, ICollection<string> target, string label)
        {
            var item = new Entry(argument, label, ImportItemKind.Mapped);
            _items.Add(item);
            _list = (argument, target, label, item, []);
        }

        public void EndList()
        {
            if (_list is not { } list) return;
            _list = null;
            if (list.Values.Count == 0)
            {
                list.Item.Kind = ImportItemKind.Dropped;
                list.Item.Description = "Not imported: no names followed it";
                return;
            }

            list.Item.Argument = list.Switch + " " + CommandLine.Join(list.Values);
            list.Item.Description = $"{list.Label}: {string.Join(", ", list.Values)}";
        }

        public void Positional(string token)
        {
            if (_list is { } list)
            {
                var value = list.Target == Options.ExcludeDirectories ? token.Trim() : Environment.ExpandEnvironmentVariables(token.Trim());
                if (value.Length == 0) return;
                list.Values.Add(value);
                if (!list.Target.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Target.Add(value);
                return;
            }

            if (_source is null)
            {
                _source = token;
                Map(CommandLine.Quote(token), "Source folder");
            }
            else if (_destination is null)
            {
                _destination = token;
                Map(CommandLine.Quote(token), "Destination folder");
            }
            else
            {
                var pattern = Environment.ExpandEnvironmentVariables(token.Trim());
                if (pattern.Length == 0) return;
                _patterns.Add(pattern);
                if (!Options.IncludeFiles.Contains(pattern, StringComparer.OrdinalIgnoreCase)) Options.IncludeFiles.Add(pattern);
                if (_patternItem is null)
                {
                    _patternItem = new Entry("", "", ImportItemKind.Mapped);
                    _items.Add(_patternItem);
                }

                _patternItem.Argument = CommandLine.Join(_patterns);
                _patternItem.Description = $"Only include files: {string.Join(", ", _patterns)}";
                if (pattern.Contains('\\') || pattern.Contains(':'))
                {
                    Note(IssueSeverity.Warning,
                        $"Robocopy treats \"{pattern}\" as a file name pattern, because it comes after the source and destination. Check that the command was written correctly.");
                }
            }
        }

        public RobocopyImportResult Finish(string? batchPath)
        {
            EndList();
            var o = Options;

            if (MoveAll || MoveFiles)
            {
                o.Mode = MoveAll ? CopyMode.MoveAll : CopyMode.MoveFiles;
                if (Mirror) Note(IssueSeverity.Warning, "/MIR can't be combined with moving files, so the profile moves files and doesn't mirror.");
                else if (Purge) Note(IssueSeverity.Warning, "/PURGE can't be combined with moving files in RoboXopy, so it was ignored.");
            }
            else if (Mirror)
            {
                o.Mode = CopyMode.Mirror;
            }
            else
            {
                o.Mode = CopyMode.Copy;
                o.Purge = Purge;
            }

            o.Subdirectories = AllSubdirectories || o.Mode == CopyMode.Mirror ? SubdirectoryMode.All
                : NonEmptySubdirectories ? SubdirectoryMode.NonEmpty
                : SubdirectoryMode.None;
            if (o.Subdirectories == SubdirectoryMode.None)
            {
                Note(IssueSeverity.Info, "The command doesn't include /S, /E or /MIR, so only files directly in the source folder are copied, not subfolders.");
            }

            o.RestartMode = (Restartable, Backup) switch
            {
                (true, true) => RestartMode.RestartableWithBackupFallback,
                (true, false) => RestartMode.Restartable,
                (false, true) => RestartMode.Backup,
                _ => RestartMode.Off,
            };

            if (!HasRetries && !HasWait)
            {
                Note(IssueSeverity.Info,
                    $"The command doesn't set /R or /W. Instead of robocopy's defaults (1,000,000 retries, 30 seconds apart), the profile retries {o.Retries} times, {o.WaitSeconds} seconds apart.");
            }
            else if (!HasRetries)
            {
                Note(IssueSeverity.Info, $"The command doesn't set /R. Instead of robocopy's default of 1,000,000 retries, the profile retries {o.Retries} times.");
            }
            else if (!HasWait)
            {
                Note(IssueSeverity.Info, $"The command doesn't set /W. Instead of robocopy's default 30-second wait between retries, the profile waits {o.WaitSeconds} seconds.");
            }

            if (_knownExtra.Count > 0)
            {
                Note(IssueSeverity.Info,
                    $"{JoinList(_knownExtra)} {(_knownExtra.Count == 1 ? "has" : "have")} no matching setting in RoboXopy, so {(_knownExtra.Count == 1 ? "it's" : "they're")} passed to robocopy as is (see Extra arguments).");
            }

            if (_unknownExtra.Count > 0)
            {
                Note(IssueSeverity.Warning,
                    $"RoboXopy doesn't recognize {JoinList(_unknownExtra)}, so {(_unknownExtra.Count == 1 ? "it's" : "they're")} passed to robocopy as is. Check that {(_unknownExtra.Count == 1 ? "it's" : "they're")} correct.");
            }

            o.ExtraArguments = CommandLine.Join(_extra);

            _profile.Source = _source is null ? "" : CleanPath(_source, "source");
            _profile.Destination = _destination is null ? "" : CleanPath(_destination, "destination");
            if (_source is null) Note(IssueSeverity.Error, "The command doesn't say which folder to copy from.");
            else if (_destination is null) Note(IssueSeverity.Error, "The command doesn't say which folder to copy to.");

            _profile.Name = SuggestName(batchPath, _profile.Source);

            var items = _items.Select(i => new ImportItem(i.Argument, i.Description, i.Kind)).ToList();
            var notes = _notes.OrderBy(n => n.Severity switch { IssueSeverity.Error => 0, IssueSeverity.Warning => 1, _ => 2 }).ToList();
            return new RobocopyImportResult(_profile, items, notes);
        }

        /// <summary>Tidies a folder argument: <c>U:\.</c> → <c>U:\</c>, trailing separators, and relative paths.</summary>
        private string CleanPath(string argument, string label)
        {
            var path = argument.Trim().Trim('"').Trim();
            if (path.EndsWith("\\.", StringComparison.Ordinal) || path.EndsWith("/.", StringComparison.Ordinal)) path = path[..^1];

            if (path.Length > 0 && !path.Contains('%') && !Path.IsPathFullyQualified(path))
            {
                if (workingDirectory is not null && Path.IsPathFullyQualified(workingDirectory))
                {
                    try
                    {
                        var full = Path.GetFullPath(path, workingDirectory);
                        Note(IssueSeverity.Warning, $"The {label} \"{argument}\" is a relative path. It was taken to mean {TrimSeparators(full)}.");
                        path = full;
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                    }
                }
                else
                {
                    Note(IssueSeverity.Warning, $"The {label} \"{argument}\" is a relative path. Enter the full path to the folder.");
                }
            }

            return TrimSeparators(path);
        }

        private static string TrimSeparators(string path)
        {
            while (path.Length > 3 && (path.EndsWith('\\') || path.EndsWith('/')) && !IsUncRoot(path)) path = path[..^1];
            return path;
        }

        private static bool IsUncRoot(string path) =>
            path.StartsWith(@"\\", StringComparison.Ordinal) && path.TrimEnd('\\').Count(c => c == '\\') <= 3;
    }
}
