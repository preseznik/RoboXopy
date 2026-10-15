using System.Windows.Threading;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.ViewModels;

namespace RoboXopy.Services;

/// <summary>
/// In-app scheduler for profiles whose schedule runner is "RoboXopy". Checks every few seconds
/// whether an occurrence fell between the previous and the current check.
/// </summary>
public sealed class AppScheduler : IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    private readonly Queue<ProfileViewModel> _queue = new();
    private readonly HashSet<Guid> _queued = [];
    private readonly HashSet<Guid> _active = [];
    private DateTime _lastCheck;

    public AppScheduler(MainViewModel main)
    {
        _main = main;
        _lastCheck = DateTime.Now;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    /// <summary>Runs the profiles that are scheduled "at sign-in" with the in-app runner.</summary>
    public void RunLogonProfiles()
    {
        if (_main.Host.Settings.SchedulerPaused) return;
        foreach (var p in _main.Profiles.ToList())
        {
            var s = p.Schedule;
            if (s.Enabled && s.Runner == ScheduleRunner.App && s.Kind == ScheduleKind.AtLogon && !p.IsBusy)
            {
                Enqueue(p);
            }
        }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var from = _lastCheck;
        _lastCheck = now;

        foreach (var p in _main.Profiles.ToList())
        {
            if (!p.IsRunning) p.RefreshExternalState();
            if (p.NextRun is { } next && next <= now) p.RefreshNextRun();
        }
        StartQueuedRuns();

        // Clock moved backwards (DST or manual change): just re-anchor.
        if (now <= from || _main.Host.Settings.SchedulerPaused) return;

        foreach (var p in _main.Profiles.ToList())
        {
            var s = p.Schedule;
            if (!s.Enabled || s.Runner != ScheduleRunner.App || s.Kind == ScheduleKind.AtLogon || p.IsBusy) continue;
            if (ScheduleCalculator.IsDue(s, from, now))
            {
                Enqueue(p);
            }
        }

        StartQueuedRuns();
    }

    private void Enqueue(ProfileViewModel profile)
    {
        if (!_queued.Add(profile.Id)) return;
        profile.SetScheduledQueued(true);
        _queue.Enqueue(profile);
        StartQueuedRuns();
    }

    private void StartQueuedRuns()
    {
        if (_main.Host.Settings.SchedulerPaused) return;
        var limit = ScheduledRunLimiter.Limit(_main.Host.Settings);
        while (_active.Count < limit && _queue.TryDequeue(out var profile))
        {
            _queued.Remove(profile.Id);
            profile.SetScheduledQueued(false);
            if (profile.IsBusy) continue;

            _active.Add(profile.Id);
            _ = RunQueuedAsync(profile);
        }
    }

    private async Task RunQueuedAsync(ProfileViewModel profile)
    {
        try
        {
            await profile.StartAsync(profile.Schedule.PreviewOnly, RunTrigger.Scheduled);
        }
        finally
        {
            _active.Remove(profile.Id);
            StartQueuedRuns();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        while (_queue.TryDequeue(out var profile)) profile.SetScheduledQueued(false);
        _queued.Clear();
    }
}
