using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>Organizes profiles into optional named groups for sequential bulk actions.</summary>
public sealed partial class ProfileGroupsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ProfileGroupsViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    public ObservableCollection<ProfileGroupItem> Groups { get; } = [];
    public bool HasGroups => Groups.Count > 0;

    public void Refresh()
    {
        var grouped = _main.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Model.Group))
            .GroupBy(profile => profile.Model.Group.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ProfileGroupItem(group.Key, group.OrderBy(profile => profile.Model.SortOrder).ToList(), _main))
            .ToList();

        Groups.Clear();
        foreach (var group in grouped) Groups.Add(group);
        OnPropertyChanged(nameof(HasGroups));
    }
}

public sealed partial class ProfileGroupItem : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Stopwatch _stopwatch = new();
    private ProfileViewModel? _activeProfile;
    private RunSessionViewModel? _activeSession;
    private IReadOnlyList<ProfileViewModel> _activeProfiles = [];
    private bool _usesByteEstimate;
    private long _totalBytes;
    private long _completedBytes;
    private int _completedProfiles;

    public ProfileGroupItem(string name, IReadOnlyList<ProfileViewModel> profiles, MainViewModel main)
    {
        Name = name;
        Profiles = profiles;
        _main = main;
    }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _activeProfileName = "";

    public string Name { get; }
    public IReadOnlyList<ProfileViewModel> Profiles { get; }
    public int ProfileCount => Profiles.Count;
    public string Detail => ProfileCount == 1 ? "1 profile" : $"{ProfileCount:N0} profiles";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Preview()
    {
        await RunSequentiallyAsync(dryRun: true);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Run()
    {
        await RunSequentiallyAsync(dryRun: false);
    }

    private bool CanStart() => !IsRunning && Profiles.Any(profile => !profile.IsBusy && !profile.HasErrors);

    partial void OnIsRunningChanged(bool value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }

    private async Task RunSequentiallyAsync(bool dryRun)
    {
        var ready = Profiles.Where(profile => !profile.IsBusy && !profile.HasErrors).ToList();
        if (ready.Count == 0)
        {
            await _main.AlertAsync("No profiles ready", "Every profile in this group is running or has validation errors.");
            return;
        }

        if (!dryRun)
        {
            var ok = await _main.ConfirmAsync(
                $"Run {ready.Count:N0} profile{(ready.Count == 1 ? "" : "s")} sequentially?",
                "Each profile runs one at a time and keeps its normal preflight and destructive-operation confirmations.",
                "Run group");
            if (!ok) return;
        }

        _activeProfiles = ready;
        _completedProfiles = 0;
        _completedBytes = 0;
        List<RunProgressEstimate?> estimates = dryRun ? [] : ready.Select(profile => profile.GetProgressEstimate()).ToList();
        _usesByteEstimate = estimates.Count == ready.Count && estimates.All(estimate => estimate is { UsesBytes: true });
        _totalBytes = _usesByteEstimate ? estimates.Sum(estimate => estimate!.Bytes) : 0;
        ProgressPercent = 0;
        ProgressText = dryRun ? $"Previewing 0 of {ready.Count:N0} profiles" : $"Running 0 of {ready.Count:N0} profiles";
        IsRunning = true;
        _stopwatch.Restart();

        try
        {
            foreach (var profile in ready)
            {
                _activeProfile = profile;
                ActiveProfileName = profile.DisplayName;
                profile.PropertyChanged += OnProfileChanged;
                AttachSession(profile.Session);
                var started = await profile.RunInteractiveAsync(dryRun);
                DetachSession();
                profile.PropertyChanged -= OnProfileChanged;

                if (_usesByteEstimate && profile.GetProgressEstimate() is { } estimate) _completedBytes += estimate.Bytes;
                _completedProfiles++;
                UpdateProgress();

                if (!started) continue;
            }
        }
        finally
        {
            DetachSession();
            if (_activeProfile is not null) _activeProfile.PropertyChanged -= OnProfileChanged;
            _activeProfile = null;
            ActiveProfileName = "";
            _stopwatch.Stop();
            ProgressPercent = 100;
            ProgressText = dryRun ? $"Previewed {_completedProfiles:N0} profiles" : $"Ran {_completedProfiles:N0} profiles";
            IsRunning = false;
        }
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.Session) && sender is ProfileViewModel profile)
        {
            AttachSession(profile.Session);
            UpdateProgress();
        }
    }

    private void AttachSession(RunSessionViewModel? session)
    {
        if (ReferenceEquals(_activeSession, session)) return;
        DetachSession();
        _activeSession = session;
        if (_activeSession is not null) _activeSession.PropertyChanged += OnSessionChanged;
    }

    private void DetachSession()
    {
        if (_activeSession is not null) _activeSession.PropertyChanged -= OnSessionChanged;
        _activeSession = null;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunSessionViewModel.ProgressPercent) or nameof(RunSessionViewModel.ByteCount)
            or nameof(RunSessionViewModel.FileCount) or nameof(RunSessionViewModel.ElapsedText))
        {
            UpdateProgress();
        }
    }

    private void UpdateProgress()
    {
        var currentFraction = Math.Clamp((_activeSession?.ProgressPercent ?? 0) / 100, 0, 1);
        if (_usesByteEstimate && _activeSession is not null && _totalBytes > 0)
        {
            ProgressPercent = Math.Clamp((_completedBytes + _activeSession.ByteCount) * 100d / _totalBytes, 0, 100);
        }
        else
        {
            ProgressPercent = _activeProfiles.Count == 0 ? 0 : Math.Clamp((_completedProfiles + currentFraction) * 100d / _activeProfiles.Count, 0, 100);
        }

        var eta = "";
        if (ProgressPercent > 0 && ProgressPercent < 100)
        {
            var remaining = TimeSpan.FromTicks((long)(_stopwatch.Elapsed.Ticks * (100 - ProgressPercent) / ProgressPercent));
            eta = $" · ≈ {RunFormatting.FormatDuration(remaining)} left";
        }

        var verb = _activeSession?.DryRun == true ? "Previewing" : "Running";
        ProgressText = $"{verb} {_completedProfiles + 1:N0} of {_activeProfiles.Count:N0}: {ActiveProfileName} · {ProgressPercent:0}%{eta}";
    }
}
