using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Models;

namespace RoboXopy.ViewModels;

/// <summary>Shows the durable source history that protects profiles from accidentally targeting an old source.</summary>
public sealed partial class SafetyCenterViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _refreshTimer;

    public SafetyCenterViewModel(MainViewModel main)
    {
        _main = main;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    public ObservableCollection<RecordedSourceItem> Sources { get; } = [];
    public ObservableCollection<RiskyDestinationItem> RiskyDestinations { get; } = [];

    [ObservableProperty] private int _acknowledgedDestinationCount;

    public bool HasSources => Sources.Count > 0;
    public bool HasRiskyDestinations => RiskyDestinations.Count > 0;
    public string SourceSummary => Sources.Count == 1 ? "1 recorded source" : $"{Sources.Count:N0} recorded sources";
    public string AcknowledgementSummary => AcknowledgedDestinationCount == 0
        ? "No risky destinations are acknowledged."
        : AcknowledgedDestinationCount == 1 ? "1 acknowledgement is active." : $"{AcknowledgedDestinationCount:N0} acknowledgements are active.";

    public void Refresh()
    {
        var profiles = _main.Profiles.ToList();
        var normalizedSources = _main.Host.Settings.SourceFolderHistory
            .Select(FolderSafety.NormalizeFolder)
            .Where(path => path is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Sources.Clear();
        foreach (var source in normalizedSources)
        {
            var currentProfiles = profiles.Where(profile =>
                string.Equals(FolderSafety.NormalizeFolder(profile.Model.Source), source, StringComparison.OrdinalIgnoreCase))
                .Select(profile => profile.DisplayName)
                .ToList();
            Sources.Add(new RecordedSourceItem(source!, currentProfiles));
        }

        RiskyDestinations.Clear();
        foreach (var profile in profiles
                     .Where(profile => FolderSafety.DestinationOverlapsSourceHistory(profile.Model.Destination, normalizedSources!))
                     .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            RiskyDestinations.Add(new RiskyDestinationItem(profile, FolderSafety.HasConfirmedHistoricalSourceDestination(profile.Model)));
        }

        AcknowledgedDestinationCount = RiskyDestinations.Count(item => item.IsAcknowledged);
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasRiskyDestinations));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(AcknowledgementSummary));
    }

    [RelayCommand]
    private void RevokeAcknowledgement(RiskyDestinationItem? item)
    {
        if (item is not { IsAcknowledged: true }) return;
        item.Profile.Model.ConfirmedHistoricalSourceDestination = null;
        item.Profile.FlushSave();
        Refresh();
    }

    [RelayCommand]
    private void OpenProfile(ProfileViewModel? profile)
    {
        if (profile is not null) _main.OpenProfile(profile);
    }
}

public sealed class RecordedSourceItem(string path, IReadOnlyList<string> currentProfiles)
{
    public string Path { get; } = path;
    public IReadOnlyList<string> CurrentProfiles { get; } = currentProfiles;
    public bool IsCurrentSource => CurrentProfiles.Count > 0;
    public string UsageText => CurrentProfiles.Count switch
    {
        0 => "Historical source",
        1 => $"Current source for {CurrentProfiles[0]}",
        _ => $"Current source for {CurrentProfiles.Count:N0} profiles",
    };
}

public sealed class RiskyDestinationItem
{
    public RiskyDestinationItem(ProfileViewModel profile, bool isAcknowledged)
    {
        Profile = profile;
        IsAcknowledged = isAcknowledged;
    }

    public ProfileViewModel Profile { get; }
    public string Destination => Profile.Model.Destination;
    public bool IsAcknowledged { get; }
    public string StateText => IsAcknowledged ? "Acknowledged" : "Confirmation required";
    public string Description => IsAcknowledged
        ? "This profile can run unattended while the destination remains unchanged."
        : "Manual operations require typing CONFIRM; unattended operations are blocked.";
}
