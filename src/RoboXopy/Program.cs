using RoboXopy.Core.Platform;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Storage;
using RoboXopy.Infrastructure;

namespace RoboXopy;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            HeadlessRunner.AttachConsole();
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run 'RoboXopy --help' for usage.");
            return HeadlessRunner.ExitCodeUsage;
        }

        // Must happen before anything touches AppPaths.Default, which resolves the data folder once.
        if (options.DataDir is not null)
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable, options.DataDir);
        }

        if (options.ShowHelp) return HeadlessRunner.PrintHelp();
        if (options.ShowVersion) return HeadlessRunner.PrintVersion();
        if (options.CheckUpdate) return HeadlessRunner.CheckUpdate();
        if (options.ExitRunning) return HeadlessRunner.ExitRunning();
        if (options.UninstallCleanup) return HeadlessRunner.UninstallCleanup();
        if (options.ListProfiles) return HeadlessRunner.ListProfiles();
        if (options.RunProfile is not null) return HeadlessRunner.Run(options);

        // An installer (Inno Setup 7) may have started us with RedirectionGuard, which every robocopy we start
        // would inherit and then skip junctions. It can't be switched off, so restart through Explorer instead.
        if (RedirectionGuard.IsEnforced && !options.Relaunched
            && Environment.ProcessPath is { } exe
            && DesktopLauncher.TryStart(exe, CommandLine.Join(args.Append("--relaunched")), Path.GetDirectoryName(exe)!))
        {
            return 0;
        }

        using var instance = new SingleInstance(AppPaths.Default.Root);
        if (!instance.IsFirstInstance)
        {
            instance.ActivateFirstInstance();
            return 0;
        }

        var app = new App(options, instance);
        app.InitializeComponent();
        return app.Run();
    }
}
