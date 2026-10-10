using System.IO;
using System.Windows.Threading;
using RoboXopy.Core.Models;
using RoboXopy.ViewModels;

namespace RoboXopy.Services;

/// <summary>
/// Watches the history folder so runs started outside this window (Task Scheduler, command line)
/// show up immediately and can raise notifications.
/// </summary>
public sealed class HistoryWatcher : IDisposable
{
    private readonly MainViewModel _main;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly HashSet<Guid> _changed = [];
    private readonly HashSet<Guid> _seenRecords = [];
    private readonly object _sync = new();

    public HistoryWatcher(MainViewModel main)
    {
        _main = main;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => Flush();

        try
        {
            var dir = main.Host.Paths.HistoryDirectory;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    /// <summary>Raised on the UI thread for a run that finished in another process.</summary>
    public event EventHandler<RunFinishedEventArgs>? ExternalRunFinished;

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(e.Name), "N", out var id)) return;
        lock (_sync) _changed.Add(id);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private async void Flush()
    {
        _debounce.Stop();
        Guid[] ids;
        lock (_sync)
        {
            ids = [.. _changed];
            _changed.Clear();
        }

        foreach (var id in ids)
        {
            var profile = _main.FindProfile(id);
            if (profile is null) continue;
            if (!profile.IsRunning) await profile.RefreshFromDiskAsync();

            IReadOnlyList<RunRecord> recent;
            try
            {
                recent = await Task.Run(() => _main.Host.History.Load(id, 5));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var record in recent.Reverse())
            {
                if (record.Trigger is not (RunTrigger.TaskScheduler or RunTrigger.CommandLine)) continue;
                if (record.EndedAt < _main.Host.StartedAt || !_seenRecords.Add(record.Id)) continue;
                ExternalRunFinished?.Invoke(this, new RunFinishedEventArgs(profile, record));
            }

            profile.RefreshExternalState();
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
