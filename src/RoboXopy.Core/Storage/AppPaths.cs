namespace RoboXopy.Core.Storage;

/// <summary>
/// Locations of RoboXopy's per-user data. Defaults to %APPDATA%\RoboXopy; the --data-dir command-line
/// option or the ROBOXOPY_DATA_DIR environment variable overrides it (useful for testing or portable use).
/// </summary>
public sealed class AppPaths
{
    public const string DataDirEnvironmentVariable = "ROBOXOPY_DATA_DIR";

    public AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Default { get; } = new(ResolveDefaultRoot());

    public static string StandardRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoboXopy");

    public string Root { get; }

    /// <summary>True when the data lives in the standard %APPDATA% location (no override is needed to find it).</summary>
    public bool IsStandardRoot => string.Equals(Normalize(Root), Normalize(StandardRoot), StringComparison.OrdinalIgnoreCase);

    /// <summary>Arguments that point a separately launched RoboXopy process (task, autostart) at this data folder.</summary>
    public string LaunchArguments
    {
        get
        {
            if (IsStandardRoot) return "";
            var root = Normalize(Root);
            // A trailing backslash would escape the closing quote (e.g. a drive root such as "D:\").
            if (root.EndsWith('\\')) root += "\\";
            return $" --data-dir \"{root}\"";
        }
    }
    public string ProfilesDirectory => Path.Combine(Root, "profiles");
    public string HistoryDirectory => Path.Combine(Root, "history");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string LocksDirectory => Path.Combine(Root, "locks");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string ProfileFile(Guid id) => Path.Combine(ProfilesDirectory, $"{id:N}.json");
    public string HistoryFile(Guid profileId) => Path.Combine(HistoryDirectory, $"{profileId:N}.jsonl");
    public string ProfileLogsDirectory(Guid profileId) => Path.Combine(LogsDirectory, profileId.ToString("N"));
    public string LockFile(Guid profileId) => Path.Combine(LocksDirectory, $"{profileId:N}.lock");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(LocksDirectory);
    }

    private static string ResolveDefaultRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridden));
        }

        return StandardRoot;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd('\\', '/') : full;
    }
}
