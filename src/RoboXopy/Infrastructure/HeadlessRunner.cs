using System.Globalization;
using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Scheduling;
using RoboXopy.Core.Storage;

namespace RoboXopy.Infrastructure;

/// <summary>Command-line mode: runs or lists profiles without creating any window.</summary>
internal static class HeadlessRunner
{
    /// <summary>Returned when RoboXopy itself could not start a run (mirrors robocopy's "serious error").</summary>
    public const int ExitCodeError = 16;
    public const int ExitCodeUsage = 16;

    private static bool _consoleAttached;

    public static void AttachConsole()
    {
        if (_consoleAttached) return;
        _consoleAttached = true;
        if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess)) return;

        // UTF-8 console encodings carry a BOM preamble, which would otherwise be printed as stray characters.
        var encoding = Console.OutputEncoding;
        if (encoding.CodePage == 65001) encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true });
        Console.WriteLine();
    }

    public static int PrintHelp()
    {
        var help = $"""
            RoboXopy {AppVersion.SemVer} – profiles, schedules and a friendly UI for robocopy

            Usage:
              RoboXopy                          Open the RoboXopy window
              RoboXopy --minimized              Start in the notification area
              RoboXopy --run <profile> [--dry-run]
                                                Run a profile without opening the window.
                                                <profile> is a profile name, id or short id.
              RoboXopy --list                   List all profiles
              RoboXopy --exit                   Close running RoboXopy windows and runs, cancelling
                                                any sync in progress
              RoboXopy --version                Show the version
              RoboXopy --help                   Show this help

            Add --data-dir <folder> to any command to use another data folder
            instead of %APPDATA%\RoboXopy (ROBOXOPY_DATA_DIR does the same).

            --run returns robocopy's exit code (0-7 = success, 8+ = failures).
            16 means RoboXopy could not start the run.
            From PowerShell, pipe to Out-Host to wait for completion:
              RoboXopy --run "My backup" | Out-Host
            """;

        AttachConsole();
        Console.WriteLine(help);
        return 0;
    }

    public static int PrintVersion()
    {
        AttachConsole();
        Console.WriteLine($"RoboXopy {AppVersion.Informational}");
        return 0;
    }

    public static int ExitRunning()
    {
        AttachConsole();
        // The window gives running syncs up to 15 seconds to stop before it closes.
        var (found, remaining) = ExitRequest.SignalOthers(TimeSpan.FromSeconds(30));
        if (found == 0) Console.WriteLine("RoboXopy is not running.");
        else if (remaining == 0) Console.WriteLine($"Closed {found} RoboXopy process{(found == 1 ? "" : "es")}.");
        else Console.Error.WriteLine($"RoboXopy: {remaining} of {found} process{(found == 1 ? "" : "es")} did not exit.");
        return remaining == 0 ? 0 : 1;
    }

    /// <summary>Removes what RoboXopy registered outside its data folder. Run by the uninstaller.</summary>
    public static int UninstallCleanup()
    {
        AttachConsole();
        var exePath = Environment.ProcessPath;
        if (exePath is null) return ExitCodeError;

        foreach (var task in TaskSchedulerService.RemoveTasksFor(exePath))
        {
            Console.WriteLine($"Removed scheduled task: {TaskSchedulerService.TaskFolder}{task}");
        }

        if (Services.StartupRegistration.RemoveFor(exePath)) Console.WriteLine("Removed the 'start with Windows' entry.");
        return 0;
    }

    public static int ListProfiles()
    {
        AttachConsole();
        var paths = AppPaths.Default;
        var profiles = new ProfileStore(paths).LoadAll();
        var history = new HistoryStore(paths);
        if (profiles.Count == 0)
        {
            Console.WriteLine($"No profiles in {paths.ProfilesDirectory}.");
            return 0;
        }

        Console.WriteLine($"{"ID",-10}{"NAME",-32}{"MODE",-12}{"SCHEDULE",-28}LAST RUN");
        foreach (var p in profiles)
        {
            var last = history.LoadLatest(p.Id);
            var lastText = last is null
                ? "never"
                : $"{last.StartedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)} {RunFormatting.OutcomeTitle(last.Outcome, last.DryRun)}";
            Console.WriteLine($"{p.ShortId,-10}{Truncate(p.Name, 30),-32}{ScheduleDescriber.DescribeMode(p.Options.Mode),-12}{Truncate(ScheduleDescriber.Describe(p.Schedule), 26),-28}{lastText}");
        }

        return 0;
    }

    public static int Run(CommandLineOptions options)
    {
        AttachConsole();
        var paths = AppPaths.Default;
        paths.EnsureCreated();
        var settings = new SettingsStore(paths).Load();

        var profile = new ProfileStore(paths).Find(options.RunProfile!);
        if (profile is null)
        {
            var message = $"Profile '{options.RunProfile}' was not found.";
            Console.Error.WriteLine($"RoboXopy: {message}");
            if (options.Trigger == RunTrigger.TaskScheduler && settings.ShowNotifications && !SingleInstance.IsGuiRunning(paths.Root))
            {
                ShowBalloon("RoboXopy – scheduled run failed", message, System.Windows.Forms.ToolTipIcon.Error);
            }

            return ExitCodeError;
        }

        var executor = new ProfileExecutor(paths, new HistoryStore(paths), () => settings);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        var exitRequested = false;
        using var exitRequest = ExitRequest.Listen(() =>
        {
            exitRequested = true;
            cts.Cancel();
        });

        Console.WriteLine($"RoboXopy: running '{profile.Name}'{(options.DryRun ? " (preview – nothing will be changed)" : "")}");
        var record = executor.ExecuteAsync(new RunRequest(profile, options.Trigger, options.DryRun), line =>
        {
            if (line.Kind != OutputLineKind.Progress) Console.WriteLine(line.Text);
        }, cts.Token).GetAwaiter().GetResult();

        Console.WriteLine($"RoboXopy: {RunFormatting.OutcomeTitle(record.Outcome, record.DryRun)} – {record.Message}");
        if (record.LogFile is not null) Console.WriteLine($"Log file: {record.LogFile}");

        if (!exitRequested && !SingleInstance.IsGuiRunning(paths.Root) && RunFormatting.ShouldNotify(record, settings))
        {
            var (title, text) = RunFormatting.Notification(record);
            ShowBalloon(title, text, AppIcons.ToolTipIconFor(record.Outcome));
        }

        return record.Outcome switch
        {
            RunOutcome.Error or RunOutcome.Cancelled => ExitCodeError,
            RunOutcome.Skipped => 0,
            _ => record.ExitCode,
        };
    }

    /// <summary>Shows a notification from a windowless process by briefly hosting a tray icon.</summary>
    private static void ShowBalloon(string title, string text, System.Windows.Forms.ToolTipIcon icon)
    {
        try
        {
            using var trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = AppIcons.LoadTrayIcon(),
                Text = "RoboXopy",
                Visible = true,
            };
            trayIcon.ShowBalloonTip(5000, title, string.IsNullOrWhiteSpace(text) ? " " : text, icon);
            var until = Environment.TickCount64 + 6000;
            while (Environment.TickCount64 < until)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(50);
            }

            trayIcon.Visible = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // Notifications are best effort.
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
