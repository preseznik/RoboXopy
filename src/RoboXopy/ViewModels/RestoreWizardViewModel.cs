using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>Runs a previewed, one-time reverse copy from a profile's backup destination to a chosen restore target.</summary>
public sealed partial class RestoreWizardViewModel : ObservableObject
{
    private readonly ProfileViewModel _profile;
    private readonly AppHost _host;
    private readonly IReadOnlyList<WizardStep> _steps =
    [
        new(0, "Backup"),
        new(1, "Target"),
        new(2, "Preview"),
        new(3, "Restore"),
    ];

    public RestoreWizardViewModel(ProfileViewModel profile, AppHost host)
    {
        _profile = profile;
        _host = host;
        BackupSource = profile.Model.Destination;
        RestoreDestination = profile.Model.Source;
        UpdateSteps();
    }

    public IReadOnlyList<WizardStep> Steps => _steps;
    public string ProfileName => _profile.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackupStep), nameof(IsTargetStep), nameof(IsPreviewStep), nameof(IsRestoreStep),
        nameof(IsFirstStep), nameof(PrimaryText))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(BackCommand))]
    private int _step;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _backupSource = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _restoreDestination = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _confirmationText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(PreviewSucceeded), nameof(PreviewResultText))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private RunRecord? _previewRecord;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestored), nameof(RestoreResultText))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private RunRecord? _restoreRecord;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(BackCommand))]
    private bool _isExecuting;

    public bool IsBackupStep => Step == 0;
    public bool IsTargetStep => Step == 1;
    public bool IsPreviewStep => Step == 2;
    public bool IsRestoreStep => Step == 3;
    public bool IsFirstStep => Step == 0;
    public bool HasPreview => PreviewRecord is not null;
    public bool PreviewSucceeded => PreviewRecord?.Outcome is RunOutcome.Success or RunOutcome.Warning;
    public bool HasRestored => RestoreRecord is not null;
    public string PreviewResultText => PreviewRecord is null ? "" : Describe(PreviewRecord);
    public string RestoreResultText => RestoreRecord is null ? "" : Describe(RestoreRecord);
    public string PrimaryText => Step switch
    {
        0 or 1 => "Next",
        2 when !HasPreview => "Run preview",
        2 => "Continue",
        _ when HasRestored => "Close",
        _ => "Restore files",
    };

    public event EventHandler? CloseRequested;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack() => Step > 0 && !IsExecuting && !HasRestored;

    [RelayCommand(CanExecute = nameof(CanPrimary))]
    private async Task Primary()
    {
        switch (Step)
        {
            case 0:
            case 1:
                Step++;
                break;
            case 2 when !HasPreview:
                await RunPreviewAsync();
                break;
            case 2:
                Step++;
                break;
            case 3 when HasRestored:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                await RestoreAsync();
                break;
        }
    }

    private bool CanPrimary() => !IsExecuting && Step switch
    {
        0 => !string.IsNullOrWhiteSpace(BackupSource),
        1 => HasDistinctFolders(),
        2 => !HasPreview || PreviewSucceeded,
        _ => HasRestored || (PreviewSucceeded && string.Equals(ConfirmationText.Trim(), "CONFIRM", StringComparison.Ordinal)),
    };

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void BrowseBackupSource()
    {
        if (ProfileViewModel.PickFolder("Choose the backup folder to restore from", BackupSource) is { } path) BackupSource = path;
    }

    [RelayCommand]
    private void BrowseRestoreDestination()
    {
        if (ProfileViewModel.PickFolder("Choose the folder to restore into", RestoreDestination) is { } path) RestoreDestination = path;
    }

    partial void OnStepChanged(int value) => UpdateSteps();

    private bool HasDistinctFolders()
    {
        var source = FolderSafety.NormalizeFolder(BackupSource);
        var destination = FolderSafety.NormalizeFolder(RestoreDestination);
        return source is not null && destination is not null && !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunPreviewAsync()
    {
        IsExecuting = true;
        try
        {
            RememberBackupSource();
            var profile = RestoreProfileFactory.Create(_profile.Model, BackupSource, RestoreDestination);
            PreviewRecord = await Task.Run(() => _host.Executor.ExecuteAsync(
                new RunRequest(profile, RunTrigger.Manual, DryRun: true, ConfirmedHistoricalSourceDestination: true)));
            await _profile.RefreshFromDiskAsync();
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task RestoreAsync()
    {
        IsExecuting = true;
        try
        {
            RememberBackupSource();
            var profile = RestoreProfileFactory.Create(_profile.Model, BackupSource, RestoreDestination);
            RestoreRecord = await Task.Run(() => _host.Executor.ExecuteAsync(
                new RunRequest(profile, RunTrigger.Manual, ConfirmedHistoricalSourceDestination: true)));
            await _profile.RefreshFromDiskAsync();
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void RememberBackupSource()
    {
        var source = FolderSafety.NormalizeFolder(BackupSource);
        if (source is null || _host.Settings.SourceFolderHistory.Any(existing =>
                string.Equals(FolderSafety.NormalizeFolder(existing), source, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _host.Settings.SourceFolderHistory = [.. _host.Settings.SourceFolderHistory, source];
        _host.SaveSettings();
    }

    private void UpdateSteps()
    {
        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == Step;
            step.IsDone = step.Index < Step;
        }
    }

    private static string Describe(RunRecord record)
    {
        var message = record.Message ?? RunFormatting.OutcomeTitle(record.Outcome, record.DryRun);
        if (record.Summary is not { } summary) return message;

        var parts = new List<string>();
        if (summary.Files.Copied > 0) parts.Add($"{summary.Files.Copied.ToString("N0", CultureInfo.CurrentCulture)} file(s) {(record.DryRun ? "to copy" : "copied")}");
        if (summary.Files.Extras > 0) parts.Add($"{summary.Files.Extras.ToString("N0", CultureInfo.CurrentCulture)} extra file(s) kept");
        if (summary.Files.Mismatch > 0) parts.Add($"{summary.Files.Mismatch.ToString("N0", CultureInfo.CurrentCulture)} mismatched");
        if (summary.Files.Failed > 0) parts.Add($"{summary.Files.Failed.ToString("N0", CultureInfo.CurrentCulture)} failed");
        return parts.Count == 0 ? message : $"{message} — {string.Join(" · ", parts)}";
    }
}
