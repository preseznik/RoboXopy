using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Updates;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>Checks GitHub for a newer release and installs it with the release's Setup.</summary>
public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly AppHost _host;
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    private readonly SemanticVersion _currentVersion;
    private UpdateClient? _client;
    private CancellationTokenSource? _downloadCts;
    private bool _cleanedUp;

    public UpdateViewModel(AppHost host, MainViewModel main)
    {
        _host = host;
        _main = main;
        _currentVersion = SemanticVersion.TryParse(AppVersion.SemVer, out var v) ? v : SemanticVersion.Parse("0.0.0");
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _timer.Tick += OnTimerTick;
        _host.Settings.PropertyChanged += OnSettingsChanged;
    }

    /// <summary>Raised when a check finds a release newer than this version.</summary>
    public event EventHandler<ReleaseInfo>? UpdateFound;

    private UpdateClient Client => _client ??= new UpdateClient(AppVersion.SemVer);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Description), nameof(IsBusy), nameof(CanCheck), nameof(ShowCheckButton),
        nameof(ShowInstallButton), nameof(ShowDownloadPageButton), nameof(ShowProgress), nameof(IsProgressIndeterminate),
        nameof(ShowCancelButton), nameof(HasUpdate), nameof(InstallButtonText), nameof(IsError), nameof(ShowReleaseNotes))]
    private UpdateState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Description), nameof(HasUpdate), nameof(NewVersion), nameof(ShowCheckButton),
        nameof(ShowInstallButton), nameof(ShowDownloadPageButton), nameof(ShowReleaseNotes), nameof(InstallButtonText),
        nameof(TrayMenuText))]
    private ReleaseInfo? _release;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private string _errorMessage = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private string _progressText = "";

    public string CurrentVersion => _currentVersion.ToString();
    public string NewVersion => Release?.Version.ToString() ?? "";
    public bool IsPortable => InstallInfo.Scope == InstallScope.Portable;

    /// <summary>A newer release is known (shown as the title-bar button and tray menu item).</summary>
    public bool HasUpdate => Release is not null && State is not UpdateState.Checking;

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public bool IsError => State == UpdateState.Failed;
    public bool CanCheck => !IsBusy;
    public bool ShowCheckButton => Release is null || State is UpdateState.Checking;
    public bool ShowInstallButton => Release is not null && !IsPortable && State is UpdateState.Available or UpdateState.Failed;
    public bool ShowDownloadPageButton => Release is not null && IsPortable && State is UpdateState.Available or UpdateState.Failed;
    public bool ShowReleaseNotes => Release is not null && !IsBusy;
    public bool ShowProgress => State is UpdateState.Downloading or UpdateState.Installing;
    public bool IsProgressIndeterminate => State == UpdateState.Installing || Progress <= 0;
    public bool ShowCancelButton => State == UpdateState.Downloading;
    public string InstallButtonText => State == UpdateState.Failed ? "Try again" : "Download and install";
    public string TrayMenuText => Release is null ? "" : $"Update to {Release.Version}…";

    public string Title => State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.UpToDate => "RoboXopy is up to date",
        UpdateState.Available => $"RoboXopy {NewVersion} is available",
        UpdateState.Downloading => $"Downloading RoboXopy {NewVersion}…",
        UpdateState.Installing => $"Installing RoboXopy {NewVersion}…",
        UpdateState.Failed when Release is not null => $"Couldn't update to {NewVersion}",
        UpdateState.Failed => "Couldn't check for updates",
        _ => $"Version {CurrentVersion}",
    };

    public string Description => State switch
    {
        UpdateState.Checking => "Asking GitHub for the latest release.",
        UpdateState.UpToDate => $"You have the latest version, {CurrentVersion}. {LastCheckedText}",
        UpdateState.Available when IsPortable =>
            $"You have {CurrentVersion}. This copy wasn't installed with Setup, so download the new version from GitHub.",
        UpdateState.Available => $"You have {CurrentVersion}.{PublishedText} RoboXopy closes while Setup runs and reopens afterwards.",
        UpdateState.Downloading => ProgressText,
        UpdateState.Installing => InstallInfo.Scope == InstallScope.AllUsers
            ? "Approve the Windows prompt if one appears. RoboXopy reopens when Setup finishes."
            : "RoboXopy reopens when Setup finishes.",
        UpdateState.Failed => ErrorMessage,
        _ => LastCheckedText,
    };

    private string PublishedText => Release?.PublishedAt is { } date ? $" {NewVersion} was released {date.LocalDateTime:d MMM yyyy}." : "";

    private string LastCheckedText
    {
        get
        {
            if (_host.Settings.LastUpdateCheck is not { } checkedAt) return "Not checked yet.";
            var local = checkedAt.LocalDateTime;
            var day = local.Date == DateTime.Today ? "today"
                : local.Date == DateTime.Today.AddDays(-1) ? "yesterday"
                : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            return $"Last checked {day} at {local.ToString("t", CultureInfo.CurrentCulture)}.";
        }
    }

    /// <summary>Starts the automatic daily check (the first one runs shortly after startup).</summary>
    public void Start() => _timer.Start();

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        var first = _timer.Interval == FirstCheckDelay;
        _timer.Interval = TimeSpan.FromHours(1);
        if (!_cleanedUp)
        {
            _cleanedUp = true;
            DeleteOldDownloads();
        }

        if (!_host.Settings.CheckForUpdates || AppVersion.IsDevBuild || IsBusy || HasUpdate) return;
        var due = first || _host.Settings.LastUpdateCheck is not { } last || DateTimeOffset.Now - last >= CheckInterval;
        if (due) await CheckAsync(manual: false);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Core.Models.AppSettings.LastUpdateCheck) && State is UpdateState.Idle or UpdateState.UpToDate)
        {
            OnPropertyChanged(nameof(Description));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNow() => CheckAsync(manual: true);

    partial void OnStateChanged(UpdateState value)
    {
        CheckNowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(TrayMenuText));
    }

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(IsProgressIndeterminate));

    public async Task CheckAsync(bool manual)
    {
        if (IsBusy) return;
        var previous = State;
        State = UpdateState.Checking;
        try
        {
            var latest = await Client.GetLatestReleaseAsync();
            _host.Settings.LastUpdateCheck = DateTimeOffset.Now;
            if (latest.Version > _currentVersion)
            {
                var isNew = Release is null || Release.Version != latest.Version;
                Release = latest;
                State = UpdateState.Available;
                if (isNew) UpdateFound?.Invoke(this, latest);
            }
            else
            {
                Release = null;
                State = UpdateState.UpToDate;
            }
        }
        catch (UpdateException ex)
        {
            // An automatic check fails quietly unless the user is looking at the result.
            if (!manual && Release is not null)
            {
                State = UpdateState.Available;
                return;
            }

            ErrorMessage = ex.Message;
            State = manual || previous is UpdateState.Idle or UpdateState.Failed ? UpdateState.Failed : previous;
        }
    }

    [RelayCommand]
    private void OpenReleasePage() => Shell.Open((Release?.PageUrl ?? Client.ReleasesPage).ToString());

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private async Task Install()
    {
        var release = Release;
        if (release is null || IsBusy) return;
        if (IsPortable)
        {
            OpenReleasePage();
            return;
        }

        var asset = release.FindInstaller(InstallInfo.Runtime);
        if (asset is null)
        {
            Fail($"Release {release.Version} has no installer for this PC ({InstallInfo.Runtime}). Download it from GitHub instead.");
            return;
        }

        // 1. Download and verify.
        Progress = 0;
        ProgressText = "Starting the download…";
        State = UpdateState.Downloading;
        string installer;
        _downloadCts = new CancellationTokenSource();
        try
        {
            var sha256 = await Client.GetExpectedSha256Async(release, asset, _downloadCts.Token);
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Fraction * 100;
                ProgressText = p.TotalBytes > 0
                    ? $"{FormatMegabytes(p.BytesReceived)} of {FormatMegabytes(p.TotalBytes)} MB"
                    : $"{FormatMegabytes(p.BytesReceived)} MB";
            });
            installer = await Client.DownloadAsync(asset, sha256, InstallInfo.DownloadFolder, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            State = UpdateState.Available;
            return;
        }
        catch (UpdateException ex)
        {
            Fail(ex.Message);
            return;
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
        }

        // 2. Setup replaces RoboXopy.exe, so every RoboXopy process has to close first.
        var running = _main.Profiles.Where(p => p.IsRunning).ToList();
        var others = ExitRequest.CountOthers();
        if (running.Count > 0 || others > 0)
        {
            if (Application.Current is App app) app.ShowMainWindow();
            var what = running.Count switch
            {
                0 => "",
                1 => $"“{running[0].DisplayName}” is still running. ",
                _ => $"{running.Count} profiles are still running. ",
            };
            if (others > 0) what += "A scheduled or command-line run of RoboXopy is also in progress. ";
            var ok = await _main.ConfirmAsync("Stop running syncs and update?",
                what + "Updating stops robocopy part-way through; the next run picks up where it left off.",
                "Stop and update", destructive: true);
            if (!ok)
            {
                State = UpdateState.Available;
                return;
            }
        }

        State = UpdateState.Installing;
        foreach (var p in _main.Profiles.Where(p => p.IsRunning)) p.Session?.CancelCommand.Execute(null);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (_main.Profiles.Any(p => p.IsRunning) && DateTime.UtcNow < deadline) await Task.Delay(100);
        // Setup's Restart Manager can't close processes without a window, such as scheduled runs.
        if (others > 0) await Task.Run(() => ExitRequest.SignalOthers(TimeSpan.FromSeconds(20)));
        _main.FlushAll();

        // 3. Run Setup. It closes this window through Restart Manager (which saves state on the way out),
        //    installs, and starts RoboXopy again.
        int exitCode;
        try
        {
            using var setup = Process.Start(new ProcessStartInfo(installer, SetupArguments()) { UseShellExecute = true })
                ?? throw new InvalidOperationException("Setup didn't start.");
            await setup.WaitForExitAsync();
            exitCode = setup.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fail(ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                ? "The Windows prompt was declined, so the update wasn't installed."
                : $"Setup couldn't start. {ex.Message}");
            return;
        }

        // Still here: Setup didn't install (or didn't close RoboXopy).
        Fail(exitCode switch
        {
            0 => "Setup finished, but RoboXopy wasn't restarted. Close RoboXopy from the tray icon and open it again.",
            2 or 5 => "Setup was cancelled, so the update wasn't installed.",
            _ => $"Setup failed (exit code {exitCode}). Details are in {Path.Combine(InstallInfo.DownloadFolder, "setup.log")}.",
        });
    }

    private static string SetupArguments()
    {
        var window = Application.Current?.MainWindow;
        var hidden = window is null || !window.IsVisible || window.WindowState == WindowState.Minimized;
        var args = new List<string>
        {
            "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL",
            "/CLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
            InstallInfo.Scope == InstallScope.AllUsers ? "/ALLUSERS" : "/CURRENTUSER",
            hidden ? "/RELAUNCH=minimized" : "/RELAUNCH=normal",
            $"\"/LOG={Path.Combine(InstallInfo.DownloadFolder, "setup.log")}\"",
        };

        var paths = Core.Storage.AppPaths.Default;
        if (!paths.IsStandardRoot) args.Add($"\"/DATADIR={Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.Root))}\"");
        return string.Join(' ', args);
    }

    private void Fail(string message)
    {
        ErrorMessage = message;
        State = UpdateState.Failed;
    }

    private static string FormatMegabytes(long bytes) => (bytes / 1048576d).ToString("0.0", CultureInfo.CurrentCulture);

    /// <summary>Removes installers left behind by earlier updates.</summary>
    private static void DeleteOldDownloads()
    {
        try
        {
            var folder = new DirectoryInfo(InstallInfo.DownloadFolder);
            if (!folder.Exists) return;
            foreach (var file in folder.EnumerateFiles("*.exe").Concat(folder.EnumerateFiles("*.partial")))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _downloadCts?.Cancel();
        _client?.Dispose();
    }
}
