using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Scheduling;
using RoboXopy.Core.Storage;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

public enum ProfileStatus
{
    Idle,
    Running,
    Success,
    Warning,
    Failed,
}

/// <summary>Editor and runner for a single <see cref="SyncProfile"/>.</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AppHost _host;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _validateTimer;
    private readonly DispatcherTimer _copiedTimer;
    private readonly SemaphoreSlim _taskLock = new(1, 1);
    private int _validationVersion;
    private bool _taskSyncPending;
    private bool _applyingRegistration;
    private bool _historyLoaded;
    private bool _deleted;

    public ProfileViewModel(SyncProfile model, MainViewModel main, AppHost host)
    {
        Model = model;
        _main = main;
        _host = host;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => FlushSave();
        _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _validateTimer.Tick += (_, _) =>
        {
            _validateTimer.Stop();
            Validate();
        };
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            CommandCopied = false;
        };

        MaxFileSize = new SizeLimitViewModel(() => Options.MaxFileSizeBytes, v => Options.MaxFileSizeBytes = v);
        MinFileSize = new SizeLimitViewModel(() => Options.MinFileSizeBytes, v => Options.MinFileSizeBytes = v);

        Model.Changed += OnModelChanged;
        _timeOfDayText = FormatTime(Schedule.TimeOfDay);
        RefreshNextRun();
        UpdateTaskStatus();
    }

    public SyncProfile Model { get; }
    public RobocopyOptions Options => Model.Options;
    public ScheduleSettings Schedule => Model.Schedule;
    public Guid Id => Model.Id;
    public AppSettings AppSettings => _host.Settings;

    public SizeLimitViewModel MaxFileSize { get; }
    public SizeLimitViewModel MinFileSize { get; }
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    // ── Display ─────────────────────────────────────────────────────────
    public string DisplayName => string.IsNullOrWhiteSpace(Model.Name) ? "Untitled profile" : Model.Name.Trim();

    /// <summary>Used by WPF for the list item's accessible name and keyboard type-ahead.</summary>
    public override string ToString() => DisplayName;
    public string ModeText => ScheduleDescriber.DescribeMode(Options.Mode);
    public string ScheduleText => ScheduleDescriber.Describe(Schedule);
    public string Subtitle => $"{ModeText} · {ScheduleText}";
    public string CommandLine => RobocopyCommandBuilder.BuildCommandLine(Model);
    public string LogsDirectory => _host.Paths.ProfileLogsDirectory(Model.Id);
    public bool IsDestructive => Options.Mode is not CopyMode.Copy || Options.Purge;
    public bool IsMirror => Options.Mode == CopyMode.Mirror;
    public bool UsesSubdirectoryOption => Options.Mode != CopyMode.Mirror;
    public bool UsesPurgeOption => Options.Mode == CopyMode.Copy;
    public bool IsTaskSchedulerRunner => Schedule.Runner == ScheduleRunner.TaskScheduler;

    public string Explanation
    {
        get
        {
            var o = Options;
            var scope = o.Mode == CopyMode.Mirror
                ? "including all subfolders"
                : o.Subdirectories switch
                {
                    SubdirectoryMode.None => "from the top folder only",
                    SubdirectoryMode.NonEmpty => "including subfolders (empty ones are skipped)",
                    _ => "including all subfolders",
                };

            var what = o.Mode switch
            {
                CopyMode.Mirror => $"Makes the destination an exact copy of the source, {scope}. Files and folders that exist only in the destination are deleted.",
                CopyMode.MoveFiles => $"Moves files to the destination, {scope}. Files are deleted from the source after they're copied; folders stay.",
                CopyMode.MoveAll => $"Moves files and folders to the destination, {scope}. Both are deleted from the source after they're copied.",
                _ when o.Purge => $"Copies new and changed files, {scope}, and deletes destination files and folders that no longer exist in the source.",
                _ => $"Copies new and changed files to the destination, {scope}. Nothing is deleted.",
            };

            var filters = new List<string>();
            if (o.IncludeFiles.Count > 0) filters.Add($"only {string.Join(", ", o.IncludeFiles.Take(4))}{(o.IncludeFiles.Count > 4 ? "…" : "")}");
            var excluded = o.ExcludeFiles.Count + o.ExcludeDirectories.Count;
            if (excluded > 0) filters.Add($"{excluded} exclusion{(excluded == 1 ? "" : "s")}");
            if (o.MaxAgeDays is > 0) filters.Add($"files changed in the last {o.MaxAgeDays} days");
            if (o.MaxFileSizeBytes is > 0) filters.Add($"files up to {RunFormatting.FormatBytes(o.MaxFileSizeBytes.Value)}");
            if (o.ExcludeOlder) filters.Add("never overwrites newer destination files");
            return filters.Count == 0 ? what : $"{what} Filters: {string.Join("; ", filters)}.";
        }
    }

    // ── Validation ──────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors), nameof(Errors), nameof(Warnings))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(PreviewCommand))]
    private IReadOnlyList<ProfileIssue> _issues = [];

    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
    public IEnumerable<ProfileIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);
    public IEnumerable<ProfileIssue> Warnings => Issues.Where(i => i.Severity != IssueSeverity.Error);

    [ObservableProperty] private string? _saveError;

    // ── Run state ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession))]
    private RunSessionViewModel? _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(PreviewCommand), nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(PreviewCommand))]
    private bool _isRunningExternally;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(LastRunText), nameof(HasLastRun), nameof(HasLastLog))]
    private RunRecord? _lastRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextRunText))]
    private DateTime? _nextRun;

    [ObservableProperty] private bool _commandCopied;
    [ObservableProperty] private bool _isHistoryLoading;

    public bool HasSession => Session is not null;
    public bool IsBusy => IsRunning || IsRunningExternally;
    public bool HasLastRun => LastRun is not null;
    public bool HasLastLog => LastRun?.LogFile is { } f && File.Exists(f);

    public ProfileStatus Status
    {
        get
        {
            if (IsBusy) return ProfileStatus.Running;
            return LastRun?.Outcome switch
            {
                RunOutcome.Success => ProfileStatus.Success,
                RunOutcome.Warning or RunOutcome.Cancelled or RunOutcome.Skipped => ProfileStatus.Warning,
                RunOutcome.Failed or RunOutcome.Error => ProfileStatus.Failed,
                _ => ProfileStatus.Idle,
            };
        }
    }

    public string LastRunText
    {
        get
        {
            if (IsRunning) return Session?.DryRun == true ? "Previewing…" : "Running…";
            if (IsRunningExternally) return "Running in another window or task…";
            if (LastRun is null) return "Never run";
            var when = ScheduleDescriber.FormatRelative(LastRun.StartedAt.LocalDateTime, DateTime.Now);
            return $"{RunFormatting.OutcomeTitle(LastRun.Outcome, LastRun.DryRun)} · {when}";
        }
    }

    public string NextRunText
    {
        get
        {
            if (!Schedule.Enabled) return "Not scheduled";
            if (Schedule.Kind == ScheduleKind.AtLogon) return "At next sign-in";
            if (Schedule.Kind == ScheduleKind.Weekly && !Schedule.SelectedDays.Any()) return "No days selected";
            if (Schedule.Runner == ScheduleRunner.App && _host.Settings.SchedulerPaused) return "Paused";
            return NextRun is { } next ? ScheduleDescriber.FormatRelative(next, DateTime.Now) : "—";
        }
    }

    // ── Task Scheduler ──────────────────────────────────────────────────
    [ObservableProperty] private bool _isSyncingTask;
    [ObservableProperty] private string? _taskError;
    [ObservableProperty] private string _taskStatusText = "";
    [ObservableProperty] private bool _isTaskRegistered;

    // ── Schedule helpers ────────────────────────────────────────────────
    [ObservableProperty] private string _timeOfDayText;

    public void CommitTimeOfDay()
    {
        if (TryParseTime(TimeOfDayText, out var time))
        {
            Schedule.TimeOfDay = time;
        }

        TimeOfDayText = FormatTime(Schedule.TimeOfDay);
    }

    partial void OnTimeOfDayTextChanged(string value)
    {
        if (TryParseTime(value, out var time) && value.Trim().Length >= 4) Schedule.TimeOfDay = time;
    }

    private static string FormatTime(TimeSpan time) => DateTime.Today.Add(time).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (TimeSpan.TryParseExact(text, ["h\\:mm", "hh\\:mm", "hhmm", "h\\:mm\\:ss", "hh\\:mm\\:ss"], CultureInfo.InvariantCulture, out time)
            && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
        {
            time = new TimeSpan(time.Hours, time.Minutes, 0);
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var dt))
        {
            time = new TimeSpan(dt.Hour, dt.Minute, 0);
            return true;
        }

        return false;
    }

    // ── Change tracking & persistence ───────────────────────────────────
    private void OnModelChanged(object? sender, string property)
    {
        if (_deleted) return;
        _saveTimer.Stop();
        _saveTimer.Start();

        if (property.StartsWith("Registered", StringComparison.Ordinal))
        {
            UpdateTaskStatus();
            return;
        }

        if (property == nameof(SyncProfile.Name)) OnPropertyChanged(nameof(DisplayName));

        if (property.StartsWith("Options.", StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(IsDestructive));
            OnPropertyChanged(nameof(IsMirror));
            OnPropertyChanged(nameof(UsesSubdirectoryOption));
            OnPropertyChanged(nameof(UsesPurgeOption));
            OnPropertyChanged(nameof(Explanation));
            if (property == "Options.MaxFileSizeBytes") MaxFileSize.Reload();
            if (property == "Options.MinFileSizeBytes") MinFileSize.Reload();
        }

        if (property.StartsWith("Schedule.", StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ScheduleText));
            OnPropertyChanged(nameof(IsTaskSchedulerRunner));
            if (property == "Schedule.TimeOfDay" && !TryParseTime(TimeOfDayText, out _)) TimeOfDayText = FormatTime(Schedule.TimeOfDay);
            RefreshNextRun();
        }

        if (property.StartsWith("Schedule.", StringComparison.Ordinal) || property == nameof(SyncProfile.Name))
        {
            _taskSyncPending = true;
            UpdateTaskStatus();
        }

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CommandLine));
        _validateTimer.Stop();
        _validateTimer.Start();
    }

    public void FlushSave()
    {
        _saveTimer.Stop();
        if (_deleted) return;
        try
        {
            _host.Profiles.Save(Model);
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = $"Changes couldn't be saved: {ex.Message}";
        }

        if (_taskSyncPending && !_applyingRegistration)
        {
            _taskSyncPending = false;
            _ = SyncTaskAsync();
        }
    }

    /// <summary>Re-checks the profile (including whether the folders exist) without blocking the UI.</summary>
    public async void Validate()
    {
        var version = ++_validationVersion;
        var snapshot = Snapshot();
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

    /// <summary>An independent deep copy, safe to hand to background threads.</summary>
    public SyncProfile Snapshot()
    {
        var json = JsonSerializer.Serialize(Model, JsonDefaults.Options);
        return JsonSerializer.Deserialize<SyncProfile>(json, JsonDefaults.Options)!;
    }

    public void MarkDeleted()
    {
        _deleted = true;
        _saveTimer.Stop();
        _validateTimer.Stop();
        Model.Changed -= OnModelChanged;
    }

    // ── Task Scheduler sync ─────────────────────────────────────────────
    public async Task SyncTaskAsync(bool force = false)
    {
        await _taskLock.WaitAsync();
        try
        {
            if (_deleted) return;
            IsSyncingTask = true;
            UpdateTaskStatus();
            var clone = Snapshot();
            if (force) clone.RegisteredTaskFingerprint = null;
            var exe = _host.ExePath;
            var tasks = _host.Tasks;
            var result = await Task.Run(() => tasks.Sync(clone, exe));

            if (_deleted) return;
            _applyingRegistration = true;
            try
            {
                Model.RegisteredTaskName = clone.RegisteredTaskName;
                Model.RegisteredTaskExePath = clone.RegisteredTaskExePath;
                Model.RegisteredTaskFingerprint = clone.RegisteredTaskFingerprint;
            }
            finally
            {
                _applyingRegistration = false;
            }

            TaskError = result is { Success: false } ? CleanSchtasksOutput(result.Output) : null;
        }
        finally
        {
            IsSyncingTask = false;
            _taskLock.Release();
            UpdateTaskStatus();
        }
    }

    /// <summary>Removes the profile's scheduled task, used when the profile is deleted.</summary>
    public async Task UnregisterTaskAsync()
    {
        await _taskLock.WaitAsync();
        try
        {
            if (string.IsNullOrEmpty(Model.RegisteredTaskName)) return;
            var clone = Snapshot();
            var tasks = _host.Tasks;
            await Task.Run(() => tasks.Unregister(clone));
        }
        finally
        {
            _taskLock.Release();
        }
    }

    private static string CleanSchtasksOutput(string output)
    {
        var text = output.Replace("ERROR:", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrEmpty(text) ? "schtasks.exe reported an error." : text;
    }

    private void UpdateTaskStatus()
    {
        var registered = !string.IsNullOrEmpty(Model.RegisteredTaskName);
        IsTaskRegistered = registered && TaskSchedulerService.IsRegistrationCurrent(Model, _host.ExePath);
        if (!TaskSchedulerService.WantsTask(Model))
        {
            TaskStatusText = registered ? "Removing the scheduled task…" : "";
            if (!IsSyncingTask && !_taskSyncPending && registered) TaskStatusText = "A scheduled task is still registered for this profile.";
            return;
        }

        if (IsSyncingTask) TaskStatusText = "Updating Windows Task Scheduler…";
        else if (TaskError is not null) TaskStatusText = "The task couldn't be registered.";
        else if (_taskSyncPending) TaskStatusText = "Saving schedule…";
        else if (IsTaskRegistered) TaskStatusText = $"Registered as {Model.RegisteredTaskName}";
        else TaskStatusText = "Not registered yet.";
    }

    public void RefreshNextRun()
    {
        NextRun = Schedule.Enabled && Schedule.Kind != ScheduleKind.AtLogon
            ? ScheduleCalculator.NextOccurrence(Schedule, DateTime.Now)
            : null;
        OnPropertyChanged(nameof(NextRunText));
    }

    // ── History ─────────────────────────────────────────────────────────
    public async Task LoadLatestAsync()
    {
        var history = _host.History;
        var id = Model.Id;
        try
        {
            LastRun = await Task.Run(() => history.LoadLatest(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task EnsureHistoryAsync(bool reload = false)
    {
        if (_historyLoaded && !reload) return;
        _historyLoaded = true;
        IsHistoryLoading = History.Count == 0;
        var history = _host.History;
        var id = Model.Id;
        try
        {
            var records = await Task.Run(() => history.Load(id, 200));
            History.Clear();
            foreach (var r in records) History.Add(new HistoryItemViewModel(r));
            if (records.Count > 0 && !IsRunning) LastRun = records[0];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    /// <summary>Called when the history file changed on disk (e.g. a Task Scheduler run finished).</summary>
    public async Task RefreshFromDiskAsync()
    {
        if (_historyLoaded) await EnsureHistoryAsync(reload: true);
        else await LoadLatestAsync();
    }

    public void RefreshExternalState()
    {
        IsRunningExternally = !IsRunning && _host.Executor.IsRunningAnywhere(Model.Id);
        OnPropertyChanged(nameof(LastRunText));
    }

    // ── Running ─────────────────────────────────────────────────────────
    private bool CanRun() => !IsBusy && !HasErrors;

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        if (IsDestructive && !(LastRun is { DryRun: false }))
        {
            var verb = Options.Mode switch
            {
                CopyMode.Mirror => "Mirroring deletes files and folders in the destination that aren't in the source.",
                CopyMode.MoveFiles or CopyMode.MoveAll => "Moving deletes the original files from the source after they're copied.",
                _ => "Purge deletes files and folders in the destination that aren't in the source.",
            };
            var ok = await _main.ConfirmAsync(
                $"Run “{DisplayName}” for the first time?",
                $"{verb}\n\nUse Preview first to see exactly what will change.",
                "Run now");
            if (!ok) return;
        }

        await StartAsync(dryRun: false, RunTrigger.Manual);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Preview() => StartAsync(dryRun: true, RunTrigger.Manual);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Session?.CancelCommand.Execute(null);

    [RelayCommand]
    private void CloseSession()
    {
        if (Session is { IsRunning: false }) Session = null;
    }

    /// <summary>Runs the profile in this process. Returns null when it was already running.</summary>
    public async Task<RunRecord?> StartAsync(bool dryRun, RunTrigger trigger)
    {
        if (IsRunning) return null;
        FlushSave();

        var snapshot = Snapshot();
        var purges = snapshot.Options.Mode == CopyMode.Mirror || (snapshot.Options.Mode == CopyMode.Copy && snapshot.Options.Purge);
        var session = new RunSessionViewModel(dryRun, trigger, RobocopyCommandBuilder.BuildCommandLine(snapshot, dryRun), purges);
        Session = session;
        IsRunning = true;
        IsRunningExternally = false;
        OnPropertyChanged(nameof(LastRunText));
        _main.NotifyRunStateChanged();

        RunRecord record;
        try
        {
            var executor = _host.Executor;
            record = await Task.Run(() => executor.ExecuteAsync(new RunRequest(snapshot, trigger, dryRun), session.Post, session.Cancellation.Token));
        }
        catch (Exception ex)
        {
            record = new RunRecord
            {
                ProfileId = Model.Id,
                ProfileName = Model.Name,
                Trigger = trigger,
                DryRun = dryRun,
                StartedAt = DateTimeOffset.Now,
                EndedAt = DateTimeOffset.Now,
                ExitCode = 16,
                Outcome = RunOutcome.Error,
                Message = ex.Message,
            };
        }

        session.Complete(record);
        IsRunning = false;
        var persisted = !(record.Outcome == RunOutcome.Skipped && trigger == RunTrigger.Manual);
        if (persisted)
        {
            LastRun = record;
            if (_historyLoaded) History.Insert(0, new HistoryItemViewModel(record));
        }
        else
        {
            RefreshExternalState();
        }

        OnPropertyChanged(nameof(LastRunText));
        RefreshNextRun();
        _main.OnRunFinished(this, record);
        return record;
    }

    // ── Folder commands ─────────────────────────────────────────────────
    [RelayCommand]
    private void BrowseSource()
    {
        if (PickFolder("Choose the source folder", Model.Source) is { } path) Model.Source = path;
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        if (PickFolder("Choose the destination folder", Model.Destination) is { } path) Model.Destination = path;
    }

    [RelayCommand]
    private void Swap()
    {
        (Model.Source, Model.Destination) = (Model.Destination, Model.Source);
    }

    [RelayCommand]
    private void OpenSource() => OpenFolder(Model.Source);

    [RelayCommand]
    private void OpenDestination() => OpenFolder(Model.Destination);

    [RelayCommand]
    private void CopyCommandLine()
    {
        try
        {
            Clipboard.SetText(CommandLine);
            CommandCopied = true;
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(LogsDirectory);
        Shell.Open(LogsDirectory);
    }

    [RelayCommand]
    private void OpenLastLog()
    {
        if (LastRun?.LogFile is { } file && File.Exists(file)) Shell.Open(file);
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        var ok = await _main.ConfirmAsync("Clear run history?", $"This removes the run history and log files for “{DisplayName}”.", "Clear history", destructive: true);
        if (!ok) return;
        try
        {
            var id = Model.Id;
            var history = _host.History;
            var logs = LogsDirectory;
            await Task.Run(() =>
            {
                history.Clear(id);
                if (Directory.Exists(logs))
                {
                    foreach (var f in Directory.EnumerateFiles(logs, "*.log"))
                    {
                        try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.AlertAsync("Couldn't clear history", ex.Message);
        }

        History.Clear();
        LastRun = null;
    }

    [RelayCommand]
    private void OpenTaskScheduler()
    {
        try
        {
            TaskSchedulerService.OpenTaskScheduler();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    [RelayCommand]
    private Task ReRegisterTask() => SyncTaskAsync(force: true);

    [RelayCommand]
    private void SetMode(CopyMode mode) => Options.Mode = mode;

    internal static string? PickFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title };
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(current ?? "");
            if (!string.IsNullOrWhiteSpace(expanded) && Directory.Exists(expanded)) dialog.InitialDirectory = expanded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    private void OpenFolder(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? "");
        if (!string.IsNullOrWhiteSpace(expanded) && Directory.Exists(expanded))
        {
            Shell.Open(expanded);
        }
        else
        {
            _ = _main.AlertAsync("Folder not found", string.IsNullOrWhiteSpace(path) ? "No folder has been chosen yet." : $"“{path}” doesn't exist or can't be reached.");
        }
    }
}
