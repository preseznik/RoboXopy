using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Controls;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>A line in the live output panel.</summary>
public sealed class OutputEntry
{
    public OutputEntry(OutputLine line)
    {
        Kind = line.Kind;
        Text = line.Text.Trim();
        Status = line.Status?.Trim();
        Name = line.Name?.Trim();
        IsExtra = line.IsExtra;
        SizeText = line.Size is { } size ? RunFormatting.FormatBytes(size) : null;
        if (string.IsNullOrEmpty(Status)) Status = Kind == OutputLineKind.Directory ? "Folder" : null;
    }

    public OutputEntry(OutputLineKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public OutputLineKind Kind { get; }
    public string Text { get; }
    public string? Status { get; }
    public string? Name { get; }
    public string? SizeText { get; }
    public bool IsExtra { get; }

    /// <summary>Used by WPF as the accessible name of the output line.</summary>
    public override string ToString() => Text;
}

/// <summary>Live state of a run started from this window: output, counters and the final result.</summary>
public sealed partial class RunSessionViewModel : ObservableObject
{
    private const int MaxEntries = 5000;
    private const int TrimCount = 1000;

    private readonly ConcurrentQueue<OutputLine> _pending = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public RunSessionViewModel(bool dryRun, RunTrigger trigger, string commandLine, bool purgesExtras = false)
    {
        DryRun = dryRun;
        Trigger = trigger;
        CommandLine = commandLine;
        ExtrasLabel = !purgesExtras ? "extra" : dryRun ? "to remove" : "removed";
        StartedAt = DateTime.Now;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Drain();
        _timer.Start();
    }

    public ObservableCollection<OutputEntry> Entries { get; } = [];
    public CancellationTokenSource Cancellation { get; } = new();
    public bool DryRun { get; }
    public RunTrigger Trigger { get; }
    public string CommandLine { get; }
    public DateTime StartedAt { get; }
    public string TitleText => DryRun ? "Previewing" : "Running";

    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private bool _isCancelling;
    [ObservableProperty] private long _fileCount;
    [ObservableProperty] private long _extraCount;
    [ObservableProperty] private long _errorCount;
    [ObservableProperty] private long _byteCount;
    [ObservableProperty] private double? _filePercent;
    [ObservableProperty] private string? _currentItem;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private RunOutcome? _outcome;
    [ObservableProperty] private string? _resultTitle;
    [ObservableProperty] private string? _resultMessage;
    [ObservableProperty] private string? _logFile;
    [ObservableProperty] private bool _trimmed;

    /// <summary>What happened (or would happen) to the files, once the run has finished with a summary.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBreakdown))]
    private IReadOnlyList<BreakdownSegment>? _breakdown;

    /// <summary>Unusual things about the run compared with the profile's history, one per line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    private string? _findingsText;

    [ObservableProperty] private bool _hasWarningFindings;
    [ObservableProperty] private bool _isResultRedundant;

    public bool HasBreakdown => Breakdown is { Count: > 0 };
    public bool HasFindings => !string.IsNullOrEmpty(FindingsText);
    public string FindingsTitle => DryRun ? "This preview looks unusual" : "This run looked unusual";

    public string BytesText => RunFormatting.FormatBytes(ByteCount);
    public string FilesLabel => DryRun ? "to copy" : "copied";
    public string ExtrasLabel { get; }

    partial void OnByteCountChanged(long value) => OnPropertyChanged(nameof(BytesText));

    /// <summary>Thread-safe; called from robocopy's output thread.</summary>
    public void Post(OutputLine line) => _pending.Enqueue(line);

    public void Complete(RunRecord record)
    {
        Drain();
        _timer.Stop();
        _stopwatch.Stop();
        ElapsedText = RunFormatting.FormatElapsed(_stopwatch.Elapsed);
        IsRunning = false;
        IsCancelling = false;
        FilePercent = null;
        CurrentItem = null;
        if (record.Summary is { } s)
        {
            // Robocopy's own totals are authoritative; keep the header in step with the result message.
            FileCount = s.Files.Copied;
            ExtraCount = s.Files.Extras;
            ByteCount = s.Bytes.Copied;
        }

        Outcome = record.Outcome;
        LogFile = record.LogFile;
        ResultTitle = RunFormatting.OutcomeTitle(record.Outcome, record.DryRun);
        Breakdown = BuildBreakdown(record);
        // The breakdown already lists the counts, so the result bar only needs robocopy's message (if any).
        var summary = Breakdown is not null && !string.IsNullOrWhiteSpace(record.Message)
            ? null
            : RunFormatting.SummaryText(record.Summary, record.DryRun, record.PurgesExtras);
        ResultMessage = string.Join(" — ", new[] { record.Message, summary }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (record.Outcome is RunOutcome.Error or RunOutcome.Skipped && !string.IsNullOrEmpty(record.Message))
        {
            Entries.Add(new OutputEntry(OutputLineKind.Error, record.Message));
        }

        // A success message only restates the exit code (copied / removed / in sync), which the breakdown already shows.
        IsResultRedundant = Breakdown is not null && record.Outcome == RunOutcome.Success;
        if (record.Findings is { Count: > 0 } findings)
        {
            HasWarningFindings = record.HasWarnings;
            FindingsText = string.Join(Environment.NewLine, findings.Select(f => f.Message));
        }
    }

    /// <summary>Splits the run's files into unchanged / copied / removed (or kept) / mismatched / failed.</summary>
    public static IReadOnlyList<BreakdownSegment>? BuildBreakdown(RunRecord record)
    {
        if (record.Summary is not { } s) return null;
        var files = s.Files;
        var bytes = s.Bytes;
        var dry = record.DryRun;
        var segments = new List<BreakdownSegment>
        {
            new(BreakdownKind.Unchanged, files.Skipped, bytes.Skipped, "unchanged"),
            new(BreakdownKind.Copied, files.Copied, bytes.Copied, dry ? "to copy" : "copied"),
            record.PurgesExtras
                ? new(BreakdownKind.Removed, files.Extras, bytes.Extras, dry ? "to remove" : "removed")
                : new(BreakdownKind.Kept, files.Extras, bytes.Extras, "extra (kept)"),
            new(BreakdownKind.Mismatch, files.Mismatch, bytes.Mismatch, "mismatched"),
            new(BreakdownKind.Failed, files.Failed, bytes.Failed, "failed"),
        };

        // Unchanged, copied and removed are always listed so the legend reads the same from run to run;
        // the rest only when they happened.
        segments.RemoveAll(x => x.Count == 0 && x.Kind is BreakdownKind.Kept or BreakdownKind.Mismatch or BreakdownKind.Failed);
        return segments.Any(x => x.Count > 0) ? segments : null;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning || IsCancelling) return;
        IsCancelling = true;
        Cancellation.Cancel();
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (!string.IsNullOrEmpty(LogFile) && File.Exists(LogFile)) Shell.Open(LogFile);
    }

    private void Drain()
    {
        if (IsRunning) ElapsedText = RunFormatting.FormatElapsed(_stopwatch.Elapsed);
        if (_pending.IsEmpty) return;

        var batch = new List<OutputLine>();
        while (_pending.TryDequeue(out var line)) batch.Add(line);

        long files = 0, extras = 0, errors = 0, bytes = 0;
        double? percent = FilePercent;
        var current = CurrentItem;
        var visible = new List<OutputEntry>();
        foreach (var line in batch)
        {
            switch (line.Kind)
            {
                case OutputLineKind.Progress:
                    percent = line.Percent;
                    continue;
                case OutputLineKind.File:
                    if (line.IsExtra)
                    {
                        extras++;
                    }
                    else if (!IsUnchanged(line.Status))
                    {
                        files++;
                        bytes += line.Size ?? 0;
                    }

                    current = line.Name;
                    percent = null;
                    break;
                case OutputLineKind.Directory:
                    current = line.Name;
                    break;
                case OutputLineKind.Error:
                    errors++;
                    break;
                case OutputLineKind.Text when string.IsNullOrWhiteSpace(line.Text) || IsRule(line.Text):
                    continue;
            }

            visible.Add(new OutputEntry(line));
        }

        FileCount += files;
        ExtraCount += extras;
        ErrorCount += errors;
        ByteCount += bytes;
        FilePercent = percent;
        CurrentItem = current;

        if (visible.Count > MaxEntries)
        {
            visible.RemoveRange(0, visible.Count - MaxEntries);
            Entries.Clear();
            Trimmed = true;
        }
        else if (Entries.Count + visible.Count > MaxEntries)
        {
            var remove = Math.Min(Entries.Count, Math.Max(TrimCount, Entries.Count + visible.Count - MaxEntries));
            for (var i = 0; i < remove; i++) Entries.RemoveAt(0);
            Trimmed = true;
        }

        foreach (var entry in visible) Entries.Add(entry);
    }

    private static bool IsUnchanged(string? status) =>
        status is not null && status.Trim().Equals("same", StringComparison.OrdinalIgnoreCase);

    private static bool IsRule(string text)
    {
        var t = text.Trim();
        return t.Length > 10 && t.All(c => c is '-' or '=');
    }
}
