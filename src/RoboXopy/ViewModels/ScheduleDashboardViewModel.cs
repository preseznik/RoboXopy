using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;

namespace RoboXopy.ViewModels;

/// <summary>Live summary of all enabled schedules, including the in-app queue and known blockers.</summary>
public sealed partial class ScheduleDashboardViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _refreshTimer;

    public ScheduleDashboardViewModel(MainViewModel main)
    {
        _main = main;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    public ObservableCollection<ScheduleDashboardItem> Items { get; } = [];

    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _queuedCount;
    [ObservableProperty] private int _scheduledCount;
    [ObservableProperty] private int _concurrencyLimit;

    public bool HasItems => Items.Count > 0;
    public bool IsSchedulerPaused => _main.Host.Settings.SchedulerPaused;
    public string QueueSummary => QueuedCount == 0
        ? $"{ActiveCount} active · queue is empty"
        : $"{ActiveCount} active · {QueuedCount} waiting";
    public string ConcurrencySummary => ConcurrencyLimit == 1
        ? "Scheduled operations run one at a time."
        : $"Up to {ConcurrencyLimit} scheduled operations may run at once.";

    public void Refresh()
    {
        var profiles = _main.Profiles
            .Where(profile => profile.Schedule.Enabled)
            .OrderBy(profile => SortOrder(profile))
            .ThenBy(profile => profile.NextRun ?? DateTime.MaxValue)
            .ThenBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Items.Clear();
        foreach (var profile in profiles) Items.Add(new ScheduleDashboardItem(profile, _main.Host.Settings));

        ScheduledCount = Items.Count;
        ActiveCount = Items.Count(item => item.IsActive);
        QueuedCount = Items.Count(item => item.IsQueued);
        ConcurrencyLimit = ScheduledRunLimiter.Limit(_main.Host.Settings);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(ConcurrencySummary));
        OnPropertyChanged(nameof(IsSchedulerPaused));
    }

    [RelayCommand]
    private void OpenProfile(ProfileViewModel? profile)
    {
        if (profile is not null) _main.OpenProfile(profile);
    }

    private static int SortOrder(ProfileViewModel profile) =>
        profile.IsScheduledQueued ? 0
        : profile.IsRunning ? 1
        : profile.HasScheduleBlocker ? 3
        : 2;
}

public sealed class ScheduleDashboardItem
{
    public ScheduleDashboardItem(ProfileViewModel profile, AppSettings settings)
    {
        Profile = profile;
        Name = profile.DisplayName;
        Runner = profile.Schedule.Runner == ScheduleRunner.TaskScheduler ? "Windows Task Scheduler" : "RoboXopy";
        IsQueued = profile.IsScheduledQueued;
        IsActive = profile.IsRunning || profile.IsRunningExternally;
        State = GetState(profile, settings);
        Detail = GetDetail(profile);
        NextRun = profile.NextRunText;
        IsPreview = profile.Schedule.PreviewOnly;
        HasBlocker = profile.HasScheduleBlocker;
        DestinationHealth = profile.LatestDestinationHealthText;
        HasDestinationHealth = profile.HasDestinationHealth;
        HasDestinationHealthProblem = profile.LatestDestinationHealthHasProblem;
    }

    public ProfileViewModel Profile { get; }
    public string Name { get; }
    public string Runner { get; }
    public string State { get; }
    public string Detail { get; }
    public string NextRun { get; }
    public bool IsQueued { get; }
    public bool IsActive { get; }
    public bool IsPreview { get; }
    public bool HasBlocker { get; }
    public string DestinationHealth { get; }
    public bool HasDestinationHealth { get; }
    public bool HasDestinationHealthProblem { get; }

    private static string GetState(ProfileViewModel profile, AppSettings settings)
    {
        if (profile.IsScheduledQueued) return "Queued";
        if (profile.IsRunning) return profile.Session?.DryRun == true ? "Previewing" : "Running";
        if (profile.IsRunningExternally) return "Running elsewhere";
        if (profile.HasScheduleBlocker) return "Needs attention";
        if (profile.Schedule.Runner == ScheduleRunner.App && settings.SchedulerPaused) return "Paused";
        return "Scheduled";
    }

    private static string GetDetail(ProfileViewModel profile)
    {
        if (profile.IsScheduledQueued) return "Waiting for an available scheduled-run slot.";
        if (profile.IsRunning) return profile.Session?.DryRun == true ? "Scheduled preview is checking for changes." : "Scheduled sync is in progress.";
        if (profile.IsRunningExternally) return "A Task Scheduler or another RoboXopy process is running this profile.";
        if (profile.HasErrors) return profile.Errors.First().Message;
        if (profile.Schedule.Kind == ScheduleKind.Weekly && !profile.Schedule.SelectedDays.Any()) return "Choose at least one day for this weekly schedule.";
        if (profile.Schedule.Runner == ScheduleRunner.TaskScheduler && !profile.IsTaskRegistered) return profile.TaskError ?? "The scheduled task is not registered.";
        return profile.Schedule.PreviewOnly ? "Runs as a preview; files are never changed automatically." : "Runs the configured sync.";
    }
}
