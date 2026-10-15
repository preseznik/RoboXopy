using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Models;

namespace RoboXopy.ViewModels;

public enum ProfileSyncType
{
    Copy,
    Mirror,
}

public sealed class ProfileCreatedByWizardEventArgs(SyncProfile profile) : EventArgs
{
    public SyncProfile Profile { get; } = profile;
}

/// <summary>Guides a first-time user through the minimum safe choices for a sync profile.</summary>
public sealed partial class ProfileCreationWizardViewModel : ObservableObject
{
    private static readonly IReadOnlyList<WizardStep> WizardSteps =
    [
        new(0, "Source"),
        new(1, "Destination"),
        new(2, "Sync type"),
        new(3, "Review"),
    ];

    private bool _nameWasGenerated = true;
    private bool _updatingGeneratedName;

    public ProfileCreationWizardViewModel(bool skipOnStartup)
    {
        SkipOnStartup = skipOnStartup;
        foreach (var step in WizardSteps)
        {
            step.IsCurrent = step.Index == 0;
            step.IsDone = false;
        }
    }

    public IReadOnlyList<WizardStep> Steps => WizardSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceStep), nameof(IsDestinationStep), nameof(IsSyncTypeStep), nameof(IsReviewStep),
        nameof(IsFirstStep), nameof(IsLastStep), nameof(PrimaryText), nameof(StepCaption))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private int _step;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _source = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _destination = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopySelected), nameof(MirrorSelected), nameof(SyncTypeDescription), nameof(SyncTypeWarning))]
    private ProfileSyncType _syncType = ProfileSyncType.Copy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _name = "";

    [ObservableProperty] private bool _skipOnStartup;

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == Steps.Count - 1;
    public bool IsSourceStep => Step == 0;
    public bool IsDestinationStep => Step == 1;
    public bool IsSyncTypeStep => Step == 2;
    public bool IsReviewStep => Step == 3;
    public string PrimaryText => IsLastStep ? "Create profile" : "Next";
    public string StepCaption => $"Step {Step + 1} of {Steps.Count}: {Steps[Step].Title}";
    public bool CopySelected => SyncType == ProfileSyncType.Copy;
    public bool MirrorSelected => SyncType == ProfileSyncType.Mirror;
    public string SyncTypeDescription => SyncType == ProfileSyncType.Mirror
        ? "The destination becomes an exact copy of the source."
        : "Copies new and changed files without deleting anything.";
    public string SyncTypeWarning => SyncType == ProfileSyncType.Mirror
        ? "Mirror removes files and folders that exist only in the destination."
        : "Copy is the safe default: destination-only files and folders are kept.";
    public string SuggestedSettings => "All subfolders · 8 copy threads · 3 retries · 5-second retry wait · junctions excluded";

    public event EventHandler<ProfileCreatedByWizardEventArgs>? ProfileCreated;
    public event EventHandler? Cancelled;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack() => Step > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (!IsLastStep)
        {
            Step++;
            return;
        }

        var profile = new SyncProfile
        {
            Name = Name.Trim(),
            Source = Source.Trim(),
            Destination = Destination.Trim(),
            Options = CreateSafeOptions(),
        };
        ProfileCreated?.Invoke(this, new ProfileCreatedByWizardEventArgs(profile));
    }

    private bool CanGoNext() => Step switch
    {
        0 => !string.IsNullOrWhiteSpace(Source),
        1 => !string.IsNullOrWhiteSpace(Destination),
        3 => !string.IsNullOrWhiteSpace(Name),
        _ => true,
    };

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void BrowseSource()
    {
        if (ProfileViewModel.PickFolder("Choose the source folder", Source) is { } path) Source = path;
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        if (ProfileViewModel.PickFolder("Choose the destination folder", Destination) is { } path) Destination = path;
    }

    [RelayCommand]
    private void SelectCopy() => SyncType = ProfileSyncType.Copy;

    [RelayCommand]
    private void SelectMirror() => SyncType = ProfileSyncType.Mirror;

    partial void OnStepChanged(int value)
    {
        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == value;
            step.IsDone = step.Index < value;
        }
    }

    partial void OnSourceChanged(string value)
    {
        if (!_nameWasGenerated) return;
        var trimmed = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmed);
        _updatingGeneratedName = true;
        Name = string.IsNullOrWhiteSpace(folderName) ? "New profile" : $"{folderName} backup";
        _updatingGeneratedName = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_updatingGeneratedName) _nameWasGenerated = false;
    }

    private RobocopyOptions CreateSafeOptions() => new()
    {
        Mode = SyncType == ProfileSyncType.Mirror ? CopyMode.Mirror : CopyMode.Copy,
        Subdirectories = SubdirectoryMode.All,
        Purge = false,
        Multithreaded = true,
        ThreadCount = 8,
        ExcludeJunctions = true,
        Retries = 3,
        WaitSeconds = 5,
    };
}
