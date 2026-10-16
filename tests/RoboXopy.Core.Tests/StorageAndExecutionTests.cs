using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Tests;

public sealed class StorageAndExecutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public StorageAndExecutionTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Profiles_RoundTrip()
    {
        var store = new ProfileStore(_paths);
        var p = new SyncProfile { Name = "Photos", Group = "Personal", Source = @"C:\Photos", Destination = @"E:\Photos" };
        p.ConfirmedHistoricalSourceDestination = @"E:\Photos";
        p.Options.VerifyAfterCopy = true;
        p.Options.Mode = CopyMode.Mirror;
        p.Options.ExcludeFiles.Add("*.tmp");
        p.Schedule.Enabled = true;
        p.Schedule.Kind = ScheduleKind.Weekly;
        p.Schedule.Saturday = true;
        store.Save(p);

        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(p.Id, loaded.Id);
        Assert.Equal("Personal", loaded.Group);
        Assert.Equal(CopyMode.Mirror, loaded.Options.Mode);
        Assert.Equal(["*.tmp"], loaded.Options.ExcludeFiles);
        Assert.True(loaded.Schedule.Saturday);
        Assert.Equal(p.Schedule.Fingerprint(), loaded.Schedule.Fingerprint());
        Assert.Equal(p.ConfirmedHistoricalSourceDestination, loaded.ConfirmedHistoricalSourceDestination);
        Assert.True(loaded.Options.VerifyAfterCopy);
        Assert.Equal(p.Id, store.Find(p.ShortId)?.Id);
        Assert.Equal(p.Id, store.Find("photos")?.Id);
        Assert.Null(store.Find("nothing"));
    }

    [Fact]
    public void ProfileChanged_FiresForNestedCollections()
    {
        var p = new SyncProfile();
        var changes = new List<string>();
        p.Changed += (_, name) => changes.Add(name);

        p.Options.ExcludeFiles.Add("*.bak");
        p.Schedule.Enabled = true;
        p.Name = "x";

        Assert.Contains("Options.ExcludeFiles", changes);
        Assert.Contains("Schedule.Enabled", changes);
        Assert.Contains("Name", changes);
    }

    [Fact]
    public void Duplicate_IsDeepAndUnregistered()
    {
        var p = new SyncProfile { Name = "A", RegisteredTaskName = @"\RoboXopy\A" };
        p.Options.IncludeFiles.Add("*.txt");
        var copy = p.Duplicate("A (copy)");

        Assert.NotEqual(p.Id, copy.Id);
        Assert.Null(copy.RegisteredTaskName);
        copy.Options.IncludeFiles.Add("*.md");
        Assert.Single(p.Options.IncludeFiles);
    }

    [Fact]
    public void History_AppendsAndReadsNewestFirst()
    {
        var history = new HistoryStore(_paths);
        var id = Guid.NewGuid();
        history.Append(new RunRecord { ProfileId = id, ExitCode = 1, Outcome = RunOutcome.Success });
        history.Append(new RunRecord { ProfileId = id, ExitCode = 8, Outcome = RunOutcome.Failed });

        var records = history.Load(id);
        Assert.Equal(2, records.Count);
        Assert.Equal(8, records[0].ExitCode);
        Assert.Equal(RunOutcome.Failed, history.LoadLatest(id)!.Outcome);
    }

    [Fact]
    public void Validator_FlagsDangerousConfigurations()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        var same = new SyncProfile { Source = src, Destination = src + "\\" };
        Assert.False(ProfileValidator.CanRun(same));

        var nested = new SyncProfile { Source = src, Destination = Path.Combine(src, "backup") };
        Assert.False(ProfileValidator.CanRun(nested));

        var mirrorIntoParent = new SyncProfile { Source = src, Destination = _root };
        mirrorIntoParent.Options.Mode = CopyMode.Mirror;
        Assert.False(ProfileValidator.CanRun(mirrorIntoParent));

        var ok = new SyncProfile { Source = src, Destination = Path.Combine(_root, "dst") };
        Assert.True(ProfileValidator.CanRun(ok));

        var missing = new SyncProfile { Source = Path.Combine(_root, "nope"), Destination = Path.Combine(_root, "dst") };
        Assert.False(ProfileValidator.CanRun(missing));
    }

    [Fact]
    public async Task Executor_RunsRobocopyAndRecordsHistory()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(src, "skip.tmp"), "temp");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), new string('x', 5000));
        var dst = Path.Combine(_root, "dst");

        var profile = new SyncProfile { Name = "Test", Source = src, Destination = dst };
        profile.Options.ExcludeFiles.Add("*.tmp");
        profile.Options.VerifyAfterCopy = true;

        var history = new HistoryStore(_paths);
        var executor = new ProfileExecutor(_paths, history, () => new AppSettings());
        var lines = new List<OutputLine>();

        var preview = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual, DryRun: true), l => { lock (lines) lines.Add(l); });
        Assert.Equal(RunOutcome.Success, preview.Outcome);
        Assert.False(File.Exists(Path.Combine(dst, "a.txt")));

        var run = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual));
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(RunOutcome.Success, run.Outcome);
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dst, "sub", "b.txt")));
        Assert.False(File.Exists(Path.Combine(dst, "skip.tmp")));
        Assert.NotNull(run.Summary);
        Assert.Equal(2, run.Summary.Files.Copied);
        Assert.True(File.Exists(run.LogFile));
        Assert.NotNull(run.Verification);
        Assert.Equal(VerificationStatus.Passed, run.Verification.Status);
        Assert.Contains("verification", File.ReadAllText(run.LogFile!), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.DestinationHealth);
        Assert.True(run.DestinationHealth.IsAvailable);
        Assert.NotNull(run.DestinationHealth.AvailableFreeBytes);
        Assert.Contains(lines, l => l.Kind == OutputLineKind.File && l.Name!.EndsWith("a.txt"));

        var again = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual));
        Assert.Equal(0, again.ExitCode);

        Assert.Equal(3, history.Load(profile.Id).Count);
        Assert.False(executor.IsRunningAnywhere(profile.Id));
    }

    [Fact]
    public async Task Executor_NamesSourceFoldersRobocopyCouldNotOpen()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src3")).FullName;
        var locked = Directory.CreateDirectory(Path.Combine(src, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "a.txt"), "a");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

        static void Icacls(string args)
        {
            using var icacls = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            icacls.WaitForExit();
            Assert.Equal(0, icacls.ExitCode);
        }

        Icacls($"\"{locked}\" /deny \"{user}:(OI)(CI)(RD)\"");
        try
        {
            var profile = new SyncProfile { Name = "Locked", Source = src, Destination = Path.Combine(_root, "dst3") };
            var executor = new ProfileExecutor(_paths, new HistoryStore(_paths), () => new AppSettings());

            var preview = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual, DryRun: true));

            Assert.Equal(RunOutcome.Failed, preview.Outcome);
            var finding = Assert.Single(preview.Findings!, f => f.Kind == RunFindingKind.UnreadableFolders);
            Assert.Contains(locked, finding.Message);
            Assert.Contains("fix their permissions", finding.Message);
            Assert.Contains("RoboXopy: skipped – ", File.ReadAllText(preview.LogFile!));
        }
        finally
        {
            Icacls($"\"{locked}\" /remove:d \"{user}\"");
        }
    }

    [Fact]
    public async Task Executor_SkipsWhenAlreadyRunning()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src2")).FullName;
        var profile = new SyncProfile { Source = src, Destination = Path.Combine(_root, "dst2") };
        var executor = new ProfileExecutor(_paths, new HistoryStore(_paths), () => new AppSettings());

        using (new FileStream(_paths.LockFile(profile.Id), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            Assert.True(executor.IsRunningAnywhere(profile.Id));
            var record = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Scheduled));
            Assert.Equal(RunOutcome.Skipped, record.Outcome);
        }

        Assert.False(executor.IsRunningAnywhere(profile.Id));
    }

    [Fact]
    public async Task Executor_BlocksHistoricalSourceDestinationWithoutConfirmation()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var previousSource = Directory.CreateDirectory(Path.Combine(_root, "previous-source")).FullName;
        var profile = new SyncProfile { Source = source, Destination = previousSource };
        var settings = new AppSettings { SourceFolderHistory = [previousSource] };
        var executor = new ProfileExecutor(_paths, new HistoryStore(_paths), () => settings);

        var blocked = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Scheduled));
        Assert.Equal(RunOutcome.Error, blocked.Outcome);
        Assert.Contains("previously used as a source", blocked.Message);

        var allowed = await executor.ExecuteAsync(new RunRequest(profile, RunTrigger.Manual, DryRun: true, ConfirmedHistoricalSourceDestination: true));
        Assert.NotEqual(RunOutcome.Error, allowed.Outcome);
    }

    [Fact]
    public void Settings_RoundTripScheduledRunConcurrency()
    {
        var settings = new AppSettings { SimultaneousScheduledRuns = true, MaxSimultaneousScheduledRuns = 3 };
        var store = new SettingsStore(_paths);
        store.Save(settings);

        var loaded = store.Load();
        Assert.True(loaded.SimultaneousScheduledRuns);
        Assert.Equal(3, loaded.MaxSimultaneousScheduledRuns);
    }

    [Fact]
    public void Settings_DefaultsStaleAndPreviewNotificationsToOff()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings { NotifyOnStaleSync = true, NotifyOnScheduledPreviewChanges = true });

        var saved = store.Load();
        Assert.True(saved.NotifyOnStaleSync);
        Assert.True(saved.NotifyOnScheduledPreviewChanges);
        Assert.False(new AppSettings().NotifyOnStaleSync);
        Assert.False(new AppSettings().NotifyOnScheduledPreviewChanges);
    }

    [Fact]
    public void Settings_PersistsAlwaysRunAsAdministrator()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings { AlwaysRunAsAdministrator = true });

        Assert.True(store.Load().AlwaysRunAsAdministrator);
        Assert.False(new AppSettings().AlwaysRunAsAdministrator);
    }
}
