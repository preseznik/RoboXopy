using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.Core.Storage;
using RoboXopy.Core.Updates;
using RoboXopy.Infrastructure;
using RoboXopy.Services;
using RoboXopy.ViewModels;
using RoboXopy.Views;

namespace RoboXopy;

public partial class App : Application
{
    private readonly CommandLineOptions _options;
    private readonly SingleInstance _instance;
    private AppScheduler? _scheduler;
    private HistoryWatcher? _watcher;
    private MainWindow? _window;
    private bool _exiting;
    private bool _exitInProgress;
    private ExitRequest? _exitRequest;

    public App(CommandLineOptions options, SingleInstance instance)
    {
        _options = options;
        _instance = instance;
    }

    public App() : this(new CommandLineOptions(), null!)
    {
    }

    public static new App Current => (App)Application.Current;

    public AppHost Host { get; private set; } = null!;
    public MainViewModel Main { get; private set; } = null!;
    public TrayIconService? Tray { get; private set; }

    public sealed record ElevationRestartResult(string? Error)
    {
        public bool Started => Error is null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);

        Host = new AppHost(AppPaths.Default);
        ThemeService.Apply(Host.Settings.Theme);

        Main = new MainViewModel(Host);
        _window = new MainWindow(Main);
        MainWindow = _window;
        _window.Closing += OnMainWindowClosing;

        var startHidden = _options.Minimized || (_options.Autostart && Host.Settings.StartMinimized);
        if (!startHidden) _window.Show();

        _instance?.ListenForActivation(() => Dispatcher.BeginInvoke(ShowMainWindow));
        // "RoboXopy --exit" (the uninstaller) closes the app without asking.
        _exitRequest = ExitRequest.Listen(() => Dispatcher.InvokeAsync(() => _ = ExitApplicationAsync(force: true)));
        // An installer that closes the app through Restart Manager reopens it in the notification area afterwards.
        NativeMethods.RegisterApplicationRestart("--minimized" + AppPaths.Default.LaunchArguments, NativeMethods.RestartOnlyAfterUpdate);

        Tray = new TrayIconService(Main, ShowMainWindow, () => _ = ExitApplicationAsync());
        Main.RunFinished += (_, args) => OnRunFinished(args, external: false);
        Main.Updates.UpdateFound += OnUpdateFound;
        Main.Updates.Start();
        Dispatcher.BeginInvoke(NotifyIfUpdated, DispatcherPriority.ApplicationIdle);
        if (!Host.Settings.SkipProfileWizard)
        {
            Dispatcher.BeginInvoke(() => Main.OpenProfileWizardCommand.Execute(null), DispatcherPriority.ApplicationIdle);
        }

        _scheduler = new AppScheduler(Main);
        _scheduler.Start();
        if (_options.Autostart) _scheduler.RunLogonProfiles();

        _watcher = new HistoryWatcher(Main);
        _watcher.ExternalRunFinished += (_, args) => OnRunFinished(args, external: true);

        if (Host.Settings.StartWithWindows) StartupRegistration.Apply(true, Host.ExePath);

        Dispatcher.BeginInvoke(SyncScheduledTasksAsync, DispatcherPriority.ApplicationIdle);
        SessionEnding += (_, _) => OnSessionEnding();
    }

    /// <summary>Logoff, shutdown, or an installer closing the app: WPF shuts down right after this returns.</summary>
    private void OnSessionEnding()
    {
        _exiting = true;
        foreach (var p in Main.Profiles.Where(p => p.IsRunning)) p.Session?.CancelCommand.Execute(null);
        SaveState();
    }

    /// <summary>Brings registered Task Scheduler tasks in line with the profiles (e.g. after the app was moved).</summary>
    private async void SyncScheduledTasksAsync()
    {
        foreach (var profile in Main.Profiles.ToList())
        {
            if (TaskSchedulerService.WantsTask(profile.Model) || !string.IsNullOrEmpty(profile.Model.RegisteredTaskName))
            {
                await profile.SyncTaskAsync();
            }
        }
    }

    private void OnRunFinished(RunFinishedEventArgs args, bool external)
    {
        var record = args.Record;
        if (!RunFormatting.ShouldNotify(record, Host.Settings)) return;
        if (!external && record.Trigger == RunTrigger.Manual && _window is { IsVisible: true, IsActive: true }) return;

        var (title, message) = RunFormatting.Notification(record);
        Tray?.ShowNotification(title, message, AppIcons.ToolTipIconFor(record.Outcome), () => ShowProfile(args.Profile));
    }

    private void OnUpdateFound(object? sender, ReleaseInfo release)
    {
        // The title bar already shows it when the window is in front.
        if (_window is { IsVisible: true, IsActive: true }) return;
        Tray?.ShowNotification($"RoboXopy {release.Version} is available",
            "Click to see what's new and install the update.", System.Windows.Forms.ToolTipIcon.Info, ShowUpdates);
    }

    private void ShowUpdates()
    {
        ShowMainWindow();
        Main.OpenUpdatesCommand.Execute(null);
    }

    /// <summary>Says so once after an update was installed, and remembers the version that ran.</summary>
    private void NotifyIfUpdated()
    {
        var current = AppVersion.SemVer;
        var last = Host.Settings.LastRunVersion;
        if (last == current) return;
        Host.Settings.LastRunVersion = current;
        if (AppVersion.IsDevBuild || !SemanticVersion.TryParse(last, out var previous) ||
            !SemanticVersion.TryParse(current, out var now) || now <= previous)
        {
            return;
        }

        Tray?.ShowNotification($"RoboXopy was updated to {now}", "Click to see what's new.", System.Windows.Forms.ToolTipIcon.Info,
            () => Shell.Open($"https://github.com/{UpdateClient.DefaultRepository}/releases/tag/v{now}"));
    }

    public void ShowProfile(ProfileViewModel profile)
    {
        if (Main.Profiles.Contains(profile))
        {
            Main.SearchText = "";
            Main.SelectedProfile = profile;
        }

        ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (_window is null || _exiting) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = _window.RestoreState;
        _window.Activate();
        var handle = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
        if (handle != 0) NativeMethods.SetForegroundWindow(handle);
    }

    public async Task<ElevationRestartResult> RestartAsAdministratorAsync()
    {
        if (_exiting) return new("RoboXopy is already closing.");

        try
        {
            var arguments = $"--wait-for-pid {Environment.ProcessId}{Host.Paths.LaunchArguments}";
            var startInfo = new ProcessStartInfo(Host.ExePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(Host.ExePath) ?? AppContext.BaseDirectory,
            };
            if (Process.Start(startInfo) is null) return new("Windows couldn't start the elevated RoboXopy process.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new(ex.Message);
        }

        await ExitApplicationAsync(force: true);
        return new(null);
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;

        if (Host.Settings.CloseToTray)
        {
            _window!.SavePlacement();
            Main.FlushAll();
            _window.Hide();
            if (!Host.Settings.TrayHintShown)
            {
                Host.Settings.TrayHintShown = true;
                Tray?.ShowNotification("RoboXopy is still running",
                    "Scheduled syncs keep running in the background. Right-click the tray icon to exit.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }

            return;
        }

        _ = ExitApplicationAsync();
    }

    /// <param name="force">Exit without asking, cancelling any running syncs (used by <c>RoboXopy --exit</c>).</param>
    public async Task ExitApplicationAsync(bool force = false)
    {
        if (_exiting || (_exitInProgress && !force)) return;
        _exitInProgress = true;
        try
        {
            var running = Main.Profiles.Where(p => p.IsRunning).ToList();
            if (running.Count > 0)
            {
                if (!force)
                {
                    ShowMainWindow();
                    var names = string.Join(", ", running.Select(p => $"“{p.DisplayName}”"));
                    var ok = await Main.ConfirmAsync("Stop running syncs and exit?",
                        $"{names} {(running.Count == 1 ? "is" : "are")} still running. Exiting stops robocopy part-way through; the next run picks up where it left off.",
                        "Stop and exit", destructive: true);
                    if (!ok || _exiting) return;
                }

                foreach (var p in running) p.Session?.CancelCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (Main.Profiles.Any(p => p.IsRunning) && DateTime.UtcNow < deadline) await Task.Delay(100);
            }

            if (_exiting) return;
            _exiting = true;
            SaveState();
            _scheduler?.Dispose();
            _watcher?.Dispose();
            Tray?.Dispose();
            Tray = null;
            _window?.Close();
            Shutdown();
        }
        finally
        {
            _exitInProgress = false;
        }
    }

    private void SaveState()
    {
        try
        {
            _window?.SavePlacement();
            Main.FlushAll();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitRequest?.Dispose();
        Main?.Updates.Dispose();
        Tray?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.Handled = true;
        var text = $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to crash.log in the data folder.";
        if (_window is { IsVisible: true }) MessageBox.Show(_window, text, "RoboXopy", MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(text, "RoboXopy", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void WriteCrashLog(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var file = Path.Combine(AppPaths.Default.Root, "crash.log");
            File.AppendAllText(file, $"[{DateTimeOffset.Now:O}] {ex}\r\n\r\n");
        }
        catch (Exception)
        {
            // Nothing more we can do.
        }
    }
}
