using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Storage;

namespace RoboXopy.ViewModels;

/// <summary>A robocopy command found in the batch file, already converted to a profile.</summary>
public sealed partial class BatchImportChoice : ObservableObject
{
    public BatchImportChoice(BatchCommand command, string? batchPath, Func<string, string> uniqueName)
    {
        Command = command;
        Result = RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory, batchPath);
        Result.Profile.Name = uniqueName(Result.Profile.Name);
    }

    public BatchCommand Command { get; }
    public RobocopyImportResult Result { get; }
    public SyncProfile Profile => Result.Profile;

    public string LineText => $"Line {Command.LineNumber}";
    public string Text => Command.Text;
    public string RawText => Command.RawText.Trim();
    public bool ShowRawText => !string.Equals(RawText.TrimStart('@').Trim(), Text.Trim(), StringComparison.Ordinal);
    public string Summary => $"{Display(Result.Profile.Source)}  →  {Display(Result.Profile.Destination)}";

    [ObservableProperty] private bool _isSelected;

    private static string Display(string path) => string.IsNullOrWhiteSpace(path) ? "(no folder)" : path;
}

public sealed partial class WizardStep(int index, string title) : ObservableObject
{
    public int Index { get; } = index;
    public string Number { get; } = (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string Title { get; } = title;
    public bool IsFirst => Index == 0;

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isDone;
}

/// <summary>Backs the "Import robocopy batch file" wizard: pick a command, review how it maps, then name the profile.</summary>
public sealed partial class BatchImportViewModel : ObservableObject
{
    private TaskCompletionSource<SyncProfile?>? _completion;
    private int _validationVersion;
    private bool _loading;

    public BatchImportViewModel()
    {
        Steps = [new WizardStep(0, "Command"), new WizardStep(1, "Settings"), new WizardStep(2, "Profile")];
        UpdateSteps();
    }

    public IReadOnlyList<WizardStep> Steps { get; }
    public ObservableCollection<BatchImportChoice> Commands { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string? _filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsLastStep), nameof(PrimaryText), nameof(StepCaption),
        nameof(IsCommandStep), nameof(IsSettingsStep), nameof(IsProfileStep))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(BackCommand))]
    private int _step;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items), nameof(Notes), nameof(BatchNotes))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private BatchImportChoice? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _name = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _source = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _destination = "";

    [ObservableProperty] private IReadOnlyList<ProfileIssue> _issues = [];
    [ObservableProperty] private string _commandPreview = "";

    public bool HasMultipleCommands => Commands.Count > 1;

    public string CommandIntro => Commands.Count > 1
        ? $"{FileName} runs robocopy {Commands.Count} times. Choose the command to turn into a profile. To import another one, import the file again."
        : $"RoboXopy found this robocopy command in {FileName}.";

    public IReadOnlyList<ImportItem> Items => Selected?.Result.Items ?? [];
    public IReadOnlyList<ImportNote> Notes => Selected?.Result.Notes ?? [];
    public IReadOnlyList<ImportNote> BatchNotes => Selected?.Command.Notes ?? [];

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == Steps.Count - 1;
    public bool IsCommandStep => Step == 0;
    public bool IsSettingsStep => Step == 1;
    public bool IsProfileStep => Step == 2;
    public string PrimaryText => IsLastStep ? "Create profile" : "Next";
    public string StepCaption => $"Step {Step + 1} of {Steps.Count}: {Steps[Step].Title}";

    /// <summary>Opens the wizard for the commands in <paramref name="batch"/>; completes with the new profile, or null if cancelled.</summary>
    public Task<SyncProfile?> ShowAsync(BatchReadResult batch, Func<string, string> uniqueName)
    {
        if (batch.Commands.Count == 0) throw new ArgumentException("The batch file has no robocopy commands.", nameof(batch));

        _completion?.TrySetResult(null);
        _completion = new TaskCompletionSource<SyncProfile?>(TaskCreationOptions.RunContinuationsAsynchronously);

        FilePath = batch.Path;
        FileName = string.IsNullOrEmpty(batch.Path) ? "The batch file" : Path.GetFileName(batch.Path);

        foreach (var choice in Commands) choice.PropertyChanged -= OnChoiceChanged;
        Commands.Clear();
        foreach (var command in batch.Commands)
        {
            var choice = new BatchImportChoice(command, batch.Path, uniqueName);
            choice.PropertyChanged += OnChoiceChanged;
            Commands.Add(choice);
        }

        OnPropertyChanged(nameof(HasMultipleCommands));
        OnPropertyChanged(nameof(CommandIntro));
        Selected = null;
        Commands[0].IsSelected = true;
        Step = 0;
        IsOpen = true;
        return _completion.Task;
    }

    public void Close(SyncProfile? result)
    {
        if (!IsOpen) return;
        IsOpen = false;
        _validationVersion++;
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack() => Step > 0;

    [RelayCommand(CanExecute = nameof(CanPrimary))]
    private void Primary()
    {
        if (!IsLastStep)
        {
            Step++;
            return;
        }

        if (Selected is null) return;
        var profile = Selected.Profile;
        profile.Name = Name.Trim();
        profile.Source = Source.Trim();
        profile.Destination = Destination.Trim();
        Close(profile);
    }

    private bool CanPrimary() => Selected is not null
        && (!IsLastStep || (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Destination)));

    [RelayCommand]
    private void Cancel() => Close(null);

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

    partial void OnStepChanged(int value) => UpdateSteps();

    partial void OnSelectedChanged(BatchImportChoice? value)
    {
        foreach (var choice in Commands)
        {
            if (choice != value) choice.IsSelected = false;
        }

        if (value is null) return;
        value.IsSelected = true;

        _loading = true;
        Name = value.Profile.Name;
        Source = value.Profile.Source;
        Destination = value.Profile.Destination;
        _loading = false;
        Refresh();
    }

    partial void OnNameChanged(string value)
    {
        if (!_loading && Selected is not null) Selected.Profile.Name = value;
    }

    partial void OnSourceChanged(string value)
    {
        if (_loading || Selected is null) return;
        Selected.Profile.Source = value;
        Refresh();
    }

    partial void OnDestinationChanged(string value)
    {
        if (_loading || Selected is null) return;
        Selected.Profile.Destination = value;
        Refresh();
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchImportChoice.IsSelected) && sender is BatchImportChoice { IsSelected: true } choice && choice != Selected)
        {
            Selected = choice;
        }
    }

    private void UpdateSteps()
    {
        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == Step;
            step.IsDone = step.Index < Step;
        }
    }

    private void Refresh()
    {
        if (Selected is null) return;
        var snapshot = JsonSerializer.Deserialize<SyncProfile>(JsonSerializer.Serialize(Selected.Profile, JsonDefaults.Options), JsonDefaults.Options)!;
        CommandPreview = RobocopyCommandBuilder.BuildCommandLine(snapshot);
        Validate(snapshot);
    }

    private async void Validate(SyncProfile snapshot)
    {
        var version = ++_validationVersion;
        IReadOnlyList<ProfileIssue> issues;
        try
        {
            issues = await Task.Run(() => ProfileValidator.Validate(snapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        if (version == _validationVersion) Issues = issues;
    }
}
