using System.Windows.Threading;
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
            if (s.Enabled && s.Runner == ScheduleRunner.App && s.Kind == ScheduleKind.AtLogon && !p.IsRunning)
            {
                _ = p.StartAsync(dryRun: false, RunTrigger.Scheduled);
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

        // Clock moved backwards (DST or manual change): just re-anchor.
        if (now <= from || _main.Host.Settings.SchedulerPaused) return;

        foreach (var p in _main.Profiles.ToList())
        {
            var s = p.Schedule;
            if (!s.Enabled || s.Runner != ScheduleRunner.App || s.Kind == ScheduleKind.AtLogon || p.IsRunning) continue;
            if (ScheduleCalculator.IsDue(s, from, now))
            {
                _ = p.StartAsync(dryRun: false, RunTrigger.Scheduled);
            }
        }
    }

    public void Dispose() => _timer.Stop();
}
