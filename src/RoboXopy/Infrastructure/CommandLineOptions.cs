using RoboXopy.Core.Models;

namespace RoboXopy.Infrastructure;

public sealed record CommandLineOptions
{
    /// <summary>Profile id, short id or name to run headlessly (no window).</summary>
    public string? RunProfile { get; init; }
    public bool DryRun { get; init; }
    public RunTrigger Trigger { get; init; } = RunTrigger.CommandLine;
    public bool Minimized { get; init; }
    public bool Autostart { get; init; }
    public bool ListProfiles { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }

    /// <summary>Print whether a newer release is available on GitHub.</summary>
    public bool CheckUpdate { get; init; }

    /// <summary>Ask running instances of this executable to quit (used by the uninstaller).</summary>
    public bool ExitRunning { get; init; }

    /// <summary>Remove scheduled tasks and the startup entry that point at this executable (used by the uninstaller).</summary>
    public bool UninstallCleanup { get; init; }

    /// <summary>Alternative data folder (profiles, history, logs, settings) instead of %APPDATA%\RoboXopy.</summary>
    public string? DataDir { get; init; }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--run" or "-r" or "/run":
                    options = options with { RunProfile = NextValue(args, ref i, arg) };
                    break;
                case "--dry-run" or "--preview" or "/l":
                    options = options with { DryRun = true };
                    break;
                case "--trigger":
                    var trigger = NextValue(args, ref i, arg).ToLowerInvariant();
                    options = options with
                    {
                        Trigger = trigger switch
                        {
                            "task" or "taskscheduler" => RunTrigger.TaskScheduler,
                            "schedule" or "scheduled" => RunTrigger.Scheduled,
                            "manual" => RunTrigger.Manual,
                            "cli" or "commandline" => RunTrigger.CommandLine,
                            _ => throw new ArgumentException($"Unknown trigger '{trigger}'."),
                        },
                    };
                    break;
                case "--minimized" or "--tray" or "-m":
                    options = options with { Minimized = true };
                    break;
                case "--autostart":
                    options = options with { Autostart = true };
                    break;
                case "--list" or "-l":
                    options = options with { ListProfiles = true };
                    break;
                case "--data-dir":
                    options = options with { DataDir = NextValue(args, ref i, arg) };
                    break;
                case "--help" or "-h" or "-?" or "/?" or "/h":
                    options = options with { ShowHelp = true };
                    break;
                case "--version" or "-v":
                    options = options with { ShowVersion = true };
                    break;
                case "--check-update" or "--check-updates":
                    options = options with { CheckUpdate = true };
                    break;
                case "--exit":
                    options = options with { ExitRunning = true };
                    break;
                case "--uninstall-cleanup":
                    options = options with { UninstallCleanup = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (options.DryRun && options.RunProfile is null)
        {
            throw new ArgumentException("--dry-run can only be used together with --run.");
        }

        return options;
    }

    private static string NextValue(IReadOnlyList<string> args, ref int i, string name)
    {
        if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
        {
            throw new ArgumentException($"{name} needs a value.");
        }

        return args[++i];
    }
}
