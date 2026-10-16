using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoboXopy.Core.Models;

/// <summary>
/// All robocopy switches RoboXopy exposes in its UI. Defaults are chosen to be safe and fast
/// for typical folder backups (rather than robocopy's own defaults of 1,000,000 retries).
/// </summary>
public sealed partial class RobocopyOptions : ObservableObject
{
    // ── Mode ────────────────────────────────────────────────────────────
    [ObservableProperty] private CopyMode _mode = CopyMode.Copy;
    [ObservableProperty] private SubdirectoryMode _subdirectories = SubdirectoryMode.All;
    [ObservableProperty] private bool _purge;

    // ── Filters ─────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _includeFiles = [];
    [ObservableProperty] private ObservableCollection<string> _excludeFiles = [];
    [ObservableProperty] private ObservableCollection<string> _excludeDirectories = [];

    [ObservableProperty] private bool _excludeOlder;
    [ObservableProperty] private bool _excludeNewer;
    [ObservableProperty] private bool _excludeChanged;
    [ObservableProperty] private bool _excludeExtra;
    [ObservableProperty] private bool _excludeLonely;
    [ObservableProperty] private bool _includeSame;
    [ObservableProperty] private bool _includeTweaked;

    [ObservableProperty] private int? _maxAgeDays;
    [ObservableProperty] private int? _minAgeDays;
    [ObservableProperty] private long? _maxFileSizeBytes;
    [ObservableProperty] private long? _minFileSizeBytes;
    [ObservableProperty] private ArchiveMode _archive = ArchiveMode.Ignore;

    // ── Copy behaviour ──────────────────────────────────────────────────
    [ObservableProperty] private bool _copyFileData = true;
    [ObservableProperty] private bool _copyFileAttributes = true;
    [ObservableProperty] private bool _copyFileTimestamps = true;
    [ObservableProperty] private bool _copyFileSecurity;
    [ObservableProperty] private bool _copyFileOwner;
    [ObservableProperty] private bool _copyFileAuditing;

    [ObservableProperty] private bool _copyDirData = true;
    [ObservableProperty] private bool _copyDirAttributes = true;
    [ObservableProperty] private bool _copyDirTimestamps = true;

    [ObservableProperty] private bool _multithreaded = true;
    [ObservableProperty] private int _threadCount = 8;
    [ObservableProperty] private RestartMode _restartMode = RestartMode.Off;
    [ObservableProperty] private bool _unbuffered;
    [ObservableProperty] private bool _compress;
    [ObservableProperty] private bool _excludeJunctions = true;
    [ObservableProperty] private bool _copySymlinksAsLinks;
    [ObservableProperty] private bool _fatFileTimes;
    [ObservableProperty] private bool _compensateDst;
    [ObservableProperty] private int? _interPacketGapMs;

    // ── Retries & logging ───────────────────────────────────────────────
    [ObservableProperty] private int _retries = 3;
    [ObservableProperty] private int _waitSeconds = 5;
    [ObservableProperty] private bool _verbose;
    [ObservableProperty] private bool _timestamps;
    [ObservableProperty] private bool _fullPaths;
    [ObservableProperty] private bool _noFileList;
    [ObservableProperty] private bool _noDirList;
    [ObservableProperty] private bool _bytes;
    [ObservableProperty] private bool _noProgress;
    /// <summary>After a real copy, re-run robocopy in list-only mode to check for remaining metadata differences.</summary>
    [ObservableProperty] private bool _verifyAfterCopy;
    [ObservableProperty] private string _extraArguments = "";

    public RobocopyOptions()
    {
        HookCollection(_includeFiles, nameof(IncludeFiles));
        HookCollection(_excludeFiles, nameof(ExcludeFiles));
        HookCollection(_excludeDirectories, nameof(ExcludeDirectories));
    }

    partial void OnIncludeFilesChanged(ObservableCollection<string>? oldValue, ObservableCollection<string> newValue)
        => Rehook(oldValue, newValue, nameof(IncludeFiles));

    partial void OnExcludeFilesChanged(ObservableCollection<string>? oldValue, ObservableCollection<string> newValue)
        => Rehook(oldValue, newValue, nameof(ExcludeFiles));

    partial void OnExcludeDirectoriesChanged(ObservableCollection<string>? oldValue, ObservableCollection<string> newValue)
        => Rehook(oldValue, newValue, nameof(ExcludeDirectories));

    private readonly Dictionary<string, NotifyCollectionChangedEventHandler> _collectionHandlers = [];

    private void Rehook(ObservableCollection<string>? oldValue, ObservableCollection<string>? newValue, string name)
    {
        if (oldValue is not null && _collectionHandlers.TryGetValue(name, out var handler))
        {
            oldValue.CollectionChanged -= handler;
        }

        if (newValue is not null)
        {
            HookCollection(newValue, name);
        }
    }

    private void HookCollection(ObservableCollection<string> collection, string name)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => OnPropertyChanged(name);
        _collectionHandlers[name] = handler;
        collection.CollectionChanged += handler;
    }

    public RobocopyOptions Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, Storage.JsonDefaults.Options);
        return System.Text.Json.JsonSerializer.Deserialize<RobocopyOptions>(json, Storage.JsonDefaults.Options)!;
    }
}
