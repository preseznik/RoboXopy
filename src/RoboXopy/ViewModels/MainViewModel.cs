using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Storage;

namespace RoboXopy.ViewModels;

public sealed class RunFinishedEventArgs(ProfileViewModel profile, RunRecord record) : EventArgs
{
    public ProfileViewModel Profile { get; } = profile;
    public RunRecord Record { get; } = record;
}

public sealed class StaleSyncStatusChangedEventArgs(ProfileViewModel profile, StaleSyncStatus previous, StaleSyncStatus current) : EventArgs
{
    public ProfileViewModel Profile { get; } = profile;
    public StaleSyncStatus Previous { get; } = previous;
    public StaleSyncStatus Current { get; } = current;
}

/// <summary>Root view model: the profile list, selection, settings page and dialogs.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _settingsSaveTimer;
    private ProfileViewModel? _profileBeforeSettings;

    public MainViewModel(AppHost host)
    {
        Host = host;
        Updates = new UpdateViewModel(host, this);
        Settings = new SettingsViewModel(host, Updates);
        ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
        ProfilesView.Filter = FilterProfile;

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            Host.SaveSettings();
        };
        Host.Settings.PropertyChanged += OnSettingsPropertyChanged;
        Dialog.PropertyChanged += OnModalPropertyChanged;
        Import.PropertyChanged += OnModalPropertyChanged;
        ScheduleDashboard = new ScheduleDashboardViewModel(this);
        SafetyCenter = new SafetyCenterViewModel(this);
        ProfileGroups = new ProfileGroupsViewModel(this);

        foreach (var profile in Host.Profiles.LoadAll().OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt))
        {
            RememberSource(profile.Source);
            Profiles.Add(CreateViewModel(profile));
        }
        Profiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(ShowEmptyState));
            ScheduleDashboard.Refresh();
            SafetyCenter.Refresh();
            ProfileGroups.Refresh();
        };

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == Host.Settings.LastSelectedProfileId) ?? Profiles.FirstOrDefault();
        foreach (var p in Profiles)
        {
            _ = p.LoadLatestAsync();
            p.Validate();
        }
    }

    public AppHost Host { get; }
    public SettingsViewModel Settings { get; }
    public UpdateViewModel Updates { get; }
    public ScheduleDashboardViewModel ScheduleDashboard { get; }
    public SafetyCenterViewModel SafetyCenter { get; }
    public ProfileGroupsViewModel ProfileGroups { get; }
    public DialogViewModel Dialog { get; } = new();
    public BatchImportViewModel Import { get; } = new();
    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];
    public ICollectionView ProfilesView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowProfile))]
    private ProfileViewModel? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowProfile), nameof(ShowScheduleDashboard), nameof(ShowSafetyCenter), nameof(ShowProfileGroups))]
    private bool _isSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowProfile), nameof(ShowScheduleDashboard), nameof(ShowSafetyCenter), nameof(ShowProfileGroups))]
    private bool _isScheduleDashboardOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowProfile), nameof(ShowScheduleDashboard), nameof(ShowSafetyCenter), nameof(ShowProfileGroups))]
    private bool _isSafetyCenterOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowProfile), nameof(ShowScheduleDashboard), nameof(ShowSafetyCenter), nameof(ShowProfileGroups))]
    private bool _isProfileGroupsOpen;

    [ObservableProperty] private string _searchText = "";

    public bool HasProfiles => Profiles.Count > 0;
    public bool ShowProfile => !IsSettingsOpen && !IsScheduleDashboardOpen && !IsSafetyCenterOpen && !IsProfileGroupsOpen && SelectedProfile is not null;
    public bool ShowEmptyState => !IsSettingsOpen && !IsScheduleDashboardOpen && !IsSafetyCenterOpen && !IsProfileGroupsOpen && SelectedProfile is null;
    public bool ShowScheduleDashboard => IsScheduleDashboardOpen;
    public bool ShowSafetyCenter => IsSafetyCenterOpen;
    public bool ShowProfileGroups => IsProfileGroupsOpen;
    public bool AnyRunning => Profiles.Any(p => p.IsRunning);

    /// <summary>A dialog or the import wizard covers the window, so the rest of it is disabled.</summary>
    public bool IsModalOpen => Dialog.IsOpen || Import.IsOpen;

    public event EventHandler<RunFinishedEventArgs>? RunFinished;
    public event EventHandler<StaleSyncStatusChangedEventArgs>? StaleSyncStatusChanged;
    public event EventHandler? RunStateChanged;
    public event EventHandler? ProfileCreated;

    public ProfileViewModel? FindProfile(Guid id) => Profiles.FirstOrDefault(p => p.Id == id);

    partial void OnSelectedProfileChanged(ProfileViewModel? value)
    {
        if (value is null) return;
        IsSettingsOpen = false;
        IsScheduleDashboardOpen = false;
        IsSafetyCenterOpen = false;
        IsProfileGroupsOpen = false;
        Host.Settings.LastSelectedProfileId = value.Id;
        value.Validate();
        value.RefreshExternalState();
        _ = value.EnsureHistoryAsync();
    }

    partial void OnSearchTextChanged(string value) => ProfilesView.Refresh();

    private bool FilterProfile(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || item is not ProfileViewModel p) return true;
        var q = SearchText.Trim();
        return p.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || p.Model.Source.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || p.Model.Destination.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private ProfileViewModel CreateViewModel(SyncProfile profile) => new(profile, this, Host);

    // ── Dialogs ─────────────────────────────────────────────────────────
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) =>
        Dialog.ShowAsync(title, message, confirmText, "Cancel", destructive);

    public Task AlertAsync(string title, string message) => Dialog.ShowAsync(title, message, "OK", null);

    public async Task<bool> ConfirmPreflightAsync(SyncProfile profile, bool dryRun)
    {
        var preflight = await Task.Run(() => ProfilePreflight.Check(profile));
        if (preflight.HasErrors)
        {
            await AlertAsync(
                dryRun ? "Preview can't start" : "Run can't start",
                string.Join(Environment.NewLine, preflight.Findings.Where(finding => finding.Severity == PreflightSeverity.Error)
                    .Select(finding => finding.Message)));
            return false;
        }

        if (!preflight.HasWarnings) return true;

        var warningText = string.Join(Environment.NewLine, preflight.Findings
            .Where(finding => finding.Severity == PreflightSeverity.Warning)
            .Select(finding => "• " + finding.Message));
        return await ConfirmAsync(
            dryRun ? "Preview preflight warning" : "Run preflight warning",
            $"{warningText}\n\nCheck the destination before continuing.",
            dryRun ? "Preview anyway" : "Run anyway",
            destructive: true);
    }

    public async Task<bool> ConfirmHistoricalSourceDestinationAsync(SyncProfile profile, bool dryRun)
    {
        if (!FolderSafety.DestinationOverlapsSourceHistory(profile.Destination, Host.Settings.SourceFolderHistory)
            || FolderSafety.HasConfirmedHistoricalSourceDestination(profile))
        {
            return true;
        }

        var confirmed = await Dialog.ShowTypedConfirmationAsync(
            dryRun ? "Preview targets a previous source" : "Destination was used as a source",
            $"“{profile.Destination}” matches or overlaps a folder RoboXopy has previously used as a source. " +
            "This can overwrite files, and mirror or purge modes can delete them. Type CONFIRM to continue.",
            dryRun ? "Confirm preview" : "Confirm run",
            "CONFIRM",
            canSkipFutureConfirmation: true);
        if (confirmed && Dialog.SkipFutureConfirmation)
        {
            profile.ConfirmedHistoricalSourceDestination = FolderSafety.NormalizeFolder(profile.Destination);
        }

        return confirmed;
    }

    public void RememberSource(string? source)
    {
        var normalized = FolderSafety.NormalizeFolder(source);
        if (normalized is null || Host.Settings.SourceFolderHistory.Any(item =>
                string.Equals(FolderSafety.NormalizeFolder(item), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Host.Settings.SourceFolderHistory = [.. Host.Settings.SourceFolderHistory, normalized];
        SafetyCenter.Refresh();
    }

    public async Task OfferRestartAsAdministratorAsync(RunRecord record)
    {
        if (!record.HasPermissionIssue || IsCurrentAdministrator()) return;

        var action = await Dialog.ShowAsync(
            record.DryRun ? "Preview needs additional permission" : "Run needs additional permission",
            "RoboXopy couldn't access one or more folders. Restarting as administrator may allow it to read or copy them. " +
            "The completed run will not be repeated automatically.",
            "Restart as administrator",
            "Not now");
        if (!action) return;

        var result = await App.Current.RestartAsAdministratorAsync();
        if (result.Error is not null)
        {
            await AlertAsync("Couldn't restart as administrator", result.Error);
        }
    }

    public bool IsAdministrator => IsCurrentAdministrator();

    [RelayCommand]
    private async Task RestartAsAdministrator()
    {
        if (IsAdministrator)
        {
            await AlertAsync("Already running as administrator", "This RoboXopy window already has administrator permissions.");
            return;
        }

        var result = await App.Current.RestartAsAdministratorAsync();
        if (result.Error is not null)
        {
            await AlertAsync("Couldn't restart as administrator", result.Error);
        }
    }

    private static bool IsCurrentAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void OnModalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DialogViewModel.IsOpen)) OnPropertyChanged(nameof(IsModalOpen));
    }

    // ── Run notifications ───────────────────────────────────────────────
    public void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(AnyRunning));
        ScheduleDashboard.Refresh();
        SafetyCenter.Refresh();
        RunStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnRunFinished(ProfileViewModel profile, RunRecord record)
    {
        NotifyRunStateChanged();
        ScheduleDashboard.Refresh();
        SafetyCenter.Refresh();
        RunFinished?.Invoke(this, new RunFinishedEventArgs(profile, record));
    }

    public void OnStaleSyncStatusChanged(ProfileViewModel profile, StaleSyncStatus previous, StaleSyncStatus current) =>
        StaleSyncStatusChanged?.Invoke(this, new StaleSyncStatusChangedEventArgs(profile, previous, current));

    // ── Settings ────────────────────────────────────────────────────────
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();

        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme):
                Services.ThemeService.Apply(Host.Settings.Theme);
                break;
            case nameof(AppSettings.StartWithWindows):
                Services.StartupRegistration.Apply(Host.Settings.StartWithWindows, Host.ExePath);
                break;
            case nameof(AppSettings.SchedulerPaused):
                foreach (var p in Profiles) p.RefreshNextRun();
                ScheduleDashboard.Refresh();
                RunStateChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(AppSettings.SourceFolderHistory):
                SafetyCenter.Refresh();
                break;
        }
    }

    public void FlushAll()
    {
        foreach (var p in Profiles) p.FlushSave();
        _settingsSaveTimer.Stop();
        Host.SaveSettings();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (IsSettingsOpen) return;
        IsScheduleDashboardOpen = false;
        IsSafetyCenterOpen = false;
        IsProfileGroupsOpen = false;
        _profileBeforeSettings = SelectedProfile;
        SelectedProfile = null;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        SelectedProfile = _profileBeforeSettings is { } p && Profiles.Contains(p) ? p : Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenScheduleDashboard()
    {
        IsSettingsOpen = false;
        IsSafetyCenterOpen = false;
        IsProfileGroupsOpen = false;
        IsScheduleDashboardOpen = true;
        ScheduleDashboard.Refresh();
    }

    [RelayCommand]
    private void OpenSafetyCenter()
    {
        IsSettingsOpen = false;
        IsScheduleDashboardOpen = false;
        IsProfileGroupsOpen = false;
        IsSafetyCenterOpen = true;
        SafetyCenter.Refresh();
    }

    [RelayCommand]
    private void OpenProfileGroups()
    {
        IsSettingsOpen = false;
        IsScheduleDashboardOpen = false;
        IsSafetyCenterOpen = false;
        IsProfileGroupsOpen = true;
        ProfileGroups.Refresh();
    }

    internal void RefreshProfileGroups() => ProfileGroups.Refresh();

    public async Task RunGroupAsync(IEnumerable<ProfileViewModel> profiles, bool dryRun)
    {
        var ready = profiles.Where(profile => !profile.IsBusy && !profile.HasErrors).ToList();
        if (ready.Count == 0)
        {
            await AlertAsync("No profiles ready", "Every profile in this group is running or has validation errors.");
            return;
        }

        if (!dryRun)
        {
            var ok = await ConfirmAsync(
                $"Run {ready.Count:N0} profile{(ready.Count == 1 ? "" : "s")} sequentially?",
                "Each profile runs one at a time and keeps its normal preflight and destructive-operation confirmations.",
                "Run group");
            if (!ok) return;
        }

        foreach (var profile in ready)
        {
            await profile.RunInteractiveAsync(dryRun);
        }
    }

    public void OpenProfile(ProfileViewModel profile)
    {
        if (!Profiles.Contains(profile)) return;
        IsSettingsOpen = false;
        IsScheduleDashboardOpen = false;
        IsSafetyCenterOpen = false;
        IsProfileGroupsOpen = false;
        SelectedProfile = profile;
        profile.Validate();
        profile.RefreshExternalState();
        _ = profile.EnsureHistoryAsync();
    }

    /// <summary>Opens Settings scrolled to the Updates section.</summary>
    [RelayCommand]
    private void OpenUpdates()
    {
        OpenSettings();
        Settings.RequestShowUpdates();
    }

    // ── Profile management ──────────────────────────────────────────────
    [RelayCommand]
    private void NewProfile()
    {
        NewBlankProfile();
    }

    [RelayCommand]
    private void OpenProfileWizard()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is null) return;
        var wizard = new Views.ProfileCreationWizardWindow(this) { Owner = owner };
        wizard.ShowDialog();
    }

    public void CreateProfileFromWizard(SyncProfile profile)
    {
        profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? "New profile" : profile.Name.Trim());
        profile.SortOrder = NextSortOrder();
        profile.CreatedAt = DateTimeOffset.Now;
        Add(profile, Profiles.Count);
        ProfileCreated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NewBlankProfile()
    {
        var profile = new SyncProfile
        {
            Name = UniqueName("New profile"),
            SortOrder = NextSortOrder(),
        };
        Add(profile, Profiles.Count);
        ProfileCreated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DuplicateProfile(ProfileViewModel? source)
    {
        source ??= SelectedProfile;
        if (source is null) return;
        source.FlushSave();
        var copy = source.Snapshot().Duplicate(UniqueName($"{source.DisplayName} (copy)"));
        copy.Schedule.Enabled = false;
        Add(copy, Profiles.IndexOf(source) + 1);
        Renumber();
    }

    [RelayCommand]
    private async Task DeleteProfile(ProfileViewModel? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null) return;
        if (profile.IsRunning)
        {
            await AlertAsync("Profile is running", $"Stop “{profile.DisplayName}” before deleting it.");
            return;
        }

        var hasTask = !string.IsNullOrEmpty(profile.Model.RegisteredTaskName);
        var ok = await ConfirmAsync(
            $"Delete “{profile.DisplayName}”?",
            "The profile, its run history and log files will be removed." + (hasTask ? " Its Windows Task Scheduler task will be deleted too." : "") +
            "\n\nFiles in the source and destination folders aren't touched.",
            "Delete",
            destructive: true);
        if (!ok) return;

        var index = Profiles.IndexOf(profile);
        profile.MarkDeleted();
        Profiles.Remove(profile);
        if (SelectedProfile is null || SelectedProfile == profile)
        {
            SelectedProfile = Profiles.Count == 0 ? null : Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];
        }

        try
        {
            Host.Profiles.Delete(profile.Id);
            Host.History.DeleteProfileData(profile.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AlertAsync("Couldn't delete everything", ex.Message);
        }

        await profile.UnregisterTaskAsync();
    }

    [RelayCommand]
    private void MoveUp(ProfileViewModel? profile) => Move(profile ?? SelectedProfile, -1);

    [RelayCommand]
    private void MoveDown(ProfileViewModel? profile) => Move(profile ?? SelectedProfile, +1);

    private void Move(ProfileViewModel? profile, int delta)
    {
        if (profile is null) return;
        var index = Profiles.IndexOf(profile);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Profiles.Count) return;
        Profiles.Move(index, target);
        Renumber();
        SelectedProfile = profile;
    }

    [RelayCommand]
    private async Task ImportProfiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import profiles",
            Filter = "RoboXopy profiles (*.json)|*.json|Robocopy batch files (*.bat, *.cmd)|*.bat;*.cmd|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;

        var imported = 0;
        var errors = new List<string>();
        var batchFiles = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (IsBatchFile(file))
            {
                batchFiles.Add(file);
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(file);
                foreach (var profile in ParseProfiles(json))
                {
                    profile.Id = Guid.NewGuid();
                    profile.RegisteredTaskName = null;
                    profile.RegisteredTaskExePath = null;
                    profile.RegisteredTaskFingerprint = null;
                    profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? "Imported profile" : profile.Name.Trim());
                    profile.SortOrder = NextSortOrder();
                    profile.CreatedAt = DateTimeOffset.Now;
                    var vm = Add(profile, Profiles.Count);
                    _ = vm.SyncTaskAsync();
                    imported++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            await AlertAsync(imported > 0 ? $"Imported {imported} profile(s) with errors" : "Import failed", string.Join("\n", errors));
        }

        foreach (var file in batchFiles)
        {
            await ImportBatchFileAsync(file);
        }
    }

    private static bool IsBatchFile(string path) =>
        Path.GetExtension(path).Equals(".bat", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task ImportBatchFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import robocopy batch file",
            Filter = "Batch files (*.bat, *.cmd)|*.bat;*.cmd|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;
        await ImportBatchFileAsync(dialog.FileName);
    }

    /// <summary>Reads a batch file and walks the user through turning its robocopy command into a profile.</summary>
    public async Task ImportBatchFileAsync(string path)
    {
        BatchReadResult batch;
        try
        {
            batch = await Task.Run(() => BatchFileReader.Read(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            await AlertAsync("Couldn't read the batch file", ex.Message);
            return;
        }

        if (batch.Commands.Count == 0)
        {
            await AlertAsync("No robocopy command found", $"RoboXopy couldn't find a line that runs robocopy in {Path.GetFileName(path)}.");
            return;
        }

        var profile = await Import.ShowAsync(batch, UniqueName);
        if (profile is null) return;

        profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? RobocopyCommandParser.DefaultName : profile.Name.Trim());
        profile.SortOrder = NextSortOrder();
        profile.CreatedAt = DateTimeOffset.Now;
        var vm = Add(profile, Profiles.Count);
        _ = vm.SyncTaskAsync();
    }

    private static IEnumerable<SyncProfile> ParseProfiles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<SyncProfile>>(json, JsonDefaults.Options) ?? [];
        }

        var single = JsonSerializer.Deserialize<SyncProfile>(json, JsonDefaults.Options);
        return single is null ? [] : [single];
    }

    [RelayCommand]
    private async Task ExportProfile(ProfileViewModel? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null) return;
        var invalid = Path.GetInvalidFileNameChars();
        var dialog = new SaveFileDialog
        {
            Title = "Export profile",
            Filter = "RoboXopy profile (*.json)|*.json",
            FileName = new string(profile.DisplayName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()) + ".json",
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;

        var snapshot = profile.Snapshot();
        snapshot.RegisteredTaskName = null;
        snapshot.RegisteredTaskExePath = null;
        snapshot.RegisteredTaskFingerprint = null;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AlertAsync("Export failed", ex.Message);
        }
    }

    private ProfileViewModel Add(SyncProfile profile, int index)
    {
        RememberSource(profile.Source);
        try
        {
            Host.Profiles.Save(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = AlertAsync("Couldn't save the profile", ex.Message);
        }

        var vm = CreateViewModel(profile);
        Profiles.Insert(Math.Clamp(index, 0, Profiles.Count), vm);
        SearchText = "";
        SelectedProfile = vm;
        vm.Validate();
        return vm;
    }

    private void Renumber()
    {
        for (var i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Model.SortOrder != i) Profiles[i].Model.SortOrder = i;
        }
    }

    private int NextSortOrder() => Profiles.Count == 0 ? 0 : Profiles.Max(p => p.Model.SortOrder) + 1;

    private string UniqueName(string baseName)
    {
        var names = Profiles.Select(p => p.DisplayName).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate)) return candidate;
        }
    }
}
