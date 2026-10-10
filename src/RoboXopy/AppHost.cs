using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.Core.Storage;

namespace RoboXopy;

/// <summary>Shared services for the running GUI instance.</summary>
public sealed class AppHost
{
    public AppHost(AppPaths paths)
    {
        Paths = paths;
        Paths.EnsureCreated();
        Profiles = new ProfileStore(paths);
        SettingsStore = new SettingsStore(paths);
        History = new HistoryStore(paths);
        Settings = SettingsStore.Load();
        Executor = new ProfileExecutor(paths, History, () => Settings);
        Tasks = new TaskSchedulerService(paths.LaunchArguments);
        ExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RoboXopy.exe");
        StartedAt = DateTimeOffset.Now;
    }

    public AppPaths Paths { get; }
    public ProfileStore Profiles { get; }
    public SettingsStore SettingsStore { get; }
    public HistoryStore History { get; }
    public AppSettings Settings { get; }
    public ProfileExecutor Executor { get; }
    public TaskSchedulerService Tasks { get; }
    public string ExePath { get; }
    public DateTimeOffset StartedAt { get; }

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are saved again on the next change or on exit.
        }
    }
}
