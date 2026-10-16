using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

public sealed partial class ProfileGroupItem(string name, IReadOnlyList<ProfileViewModel> profiles, MainViewModel main) : ObservableObject
{
    [ObservableProperty] private bool _isRunning;

    public string Name { get; } = name;
    public IReadOnlyList<ProfileViewModel> Profiles { get; } = profiles;
    public int ProfileCount => Profiles.Count;
    public string Detail => ProfileCount == 1 ? "1 profile" : $"{ProfileCount:N0} profiles";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Preview()
    {
        IsRunning = true;
        try { await main.RunGroupAsync(Profiles, dryRun: true); }
        finally { IsRunning = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Run()
    {
        IsRunning = true;
        try { await main.RunGroupAsync(Profiles, dryRun: false); }
        finally { IsRunning = false; }
    }

    private bool CanStart() => !IsRunning && Profiles.Any(profile => !profile.IsBusy && !profile.HasErrors);

    partial void OnIsRunningChanged(bool value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }
}
