using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RoboXopy.Core.Insights;
using RoboXopy.Core.Models;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

public sealed record ChartRange(string Label, int Runs)
{
    public override string ToString() => Label;
}

/// <summary>State and summary figures for the activity chart on a profile's History tab.</summary>
public sealed partial class HistoryChartViewModel : ObservableObject
{
    /// <summary>Most records kept for the chart (the list shows fewer).</summary>
    public const int MaxRecords = 500;

    private readonly List<RunRecord> _records = [];

    public static IReadOnlyList<ChartRange> Ranges { get; } =
    [
        new("Last 10 runs", 10),
        new("Last 30 runs", 30),
        new("Last 100 runs", 100),
        new("All runs", 0),
    ];

    [ObservableProperty] private ChartRange _selectedRange = Ranges[1];
    [ObservableProperty] private HistoryMetric _metric = HistoryMetric.Bytes;
    [ObservableProperty] private bool _includePreviews;
    [ObservableProperty] private HistorySeries _series = HistorySeries.Empty;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _hasPreviews;
    [ObservableProperty] private bool _showsRemoved;

    partial void OnSelectedRangeChanged(ChartRange value) => Rebuild();
    partial void OnMetricChanged(HistoryMetric value) => Rebuild();
    partial void OnIncludePreviewsChanged(bool value) => Rebuild();

    public IReadOnlyList<HistoryPoint> Points => Series.Points;
    private HistoryStats Stats => Series.Stats;
    private bool ByFiles => Metric == HistoryMetric.Files;

    public string CopiedValue => ByFiles ? FormatFiles(Stats.CopiedFiles) : RunFormatting.FormatBytes(Stats.CopiedBytes);
    public string CopiedDetail => ByFiles ? RunFormatting.FormatBytes(Stats.CopiedBytes) : FormatFiles(Stats.CopiedFiles);
    public string RemovedValue => ByFiles ? FormatFiles(Stats.RemovedFiles) : RunFormatting.FormatBytes(Stats.RemovedBytes);
    public string RemovedDetail => ByFiles ? RunFormatting.FormatBytes(Stats.RemovedBytes) : FormatFiles(Stats.RemovedFiles);
    public string RunsValue => Stats.Runs.ToString("N0", CultureInfo.CurrentCulture);
    public bool HasProblems => Stats.Problems > 0;
    public string RunsDetail => Stats.Problems switch
    {
        0 => "None failed",
        1 => "1 failed",
        var n => $"{n:N0} failed",
    };

    public string DurationValue => Stats.AverageDuration is { } d ? RunFormatting.FormatDuration(d) : "–";
    public string DurationDetail => "Average per run";

    public string SourceValue => (ByFiles ? Stats.SourceFiles : Stats.SourceBytes) is { } v
        ? ByFiles ? FormatFiles(v) : RunFormatting.FormatBytes(v)
        : "–";

    public string SourceDetail
    {
        get
        {
            var change = ByFiles ? Stats.SourceFilesChange : Stats.SourceBytesChange;
            if (change is not { } c) return "Latest run";
            if (c == 0) return "No change in range";
            var amount = ByFiles ? FormatFiles(Math.Abs(c)) : RunFormatting.FormatBytes(Math.Abs(c));
            return $"{(c > 0 ? "+" : "−")}{amount} in range";
        }
    }

    /// <summary>Plain-text summary of the chart for screen readers.</summary>
    public string Description
    {
        get
        {
            if (Stats.Runs == 0) return "No runs in this range.";
            var culture = CultureInfo.CurrentCulture;
            var parts = new List<string>
            {
                string.Create(culture, $"{Stats.Runs:N0} runs from {Stats.From?.LocalDateTime:d} to {Stats.To?.LocalDateTime:d}."),
                $"Copied {RunFormatting.FormatBytes(Stats.CopiedBytes)} ({FormatFiles(Stats.CopiedFiles)}).",
            };
            if (ShowsRemoved) parts.Add($"Removed {RunFormatting.FormatBytes(Stats.RemovedBytes)} ({FormatFiles(Stats.RemovedFiles)}).");
            if (Stats.Problems > 0) parts.Add($"{RunsDetail}.");
            if (Stats.SourceBytes is { } source) parts.Add($"The source is {RunFormatting.FormatBytes(source)}.");
            var flagged = Points.Count(p => p.Run.HasWarnings);
            if (flagged > 0) parts.Add(flagged == 1 ? "1 run looked unusual." : $"{flagged} runs looked unusual.");
            parts.Add("Use the arrow keys to read individual runs.");
            return string.Join(" ", parts);
        }
    }

    /// <summary>Replaces the records (any order).</summary>
    public void SetRecords(IEnumerable<RunRecord> records)
    {
        _records.Clear();
        _records.AddRange(records.OrderByDescending(r => r.StartedAt).Take(MaxRecords));
        Rebuild();
    }

    public void Add(RunRecord record)
    {
        _records.Insert(0, record);
        if (_records.Count > MaxRecords) _records.RemoveAt(_records.Count - 1);
        Rebuild();
    }

    private void Rebuild()
    {
        IsAvailable = HistorySeries.ChartableRuns(_records) >= 2;
        HasPreviews = _records.Any(r => r.DryRun && r.Summary is not null);
        ShowsRemoved = _records.Any(r => r.PurgesExtras && !r.DryRun);
        Series = IsAvailable
            ? HistorySeries.Build(_records, Metric, SelectedRange.Runs, IncludePreviews && HasPreviews)
            : HistorySeries.Empty;

        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(CopiedValue));
        OnPropertyChanged(nameof(CopiedDetail));
        OnPropertyChanged(nameof(RemovedValue));
        OnPropertyChanged(nameof(RemovedDetail));
        OnPropertyChanged(nameof(RunsValue));
        OnPropertyChanged(nameof(RunsDetail));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(DurationValue));
        OnPropertyChanged(nameof(SourceValue));
        OnPropertyChanged(nameof(SourceDetail));
        OnPropertyChanged(nameof(Description));
    }

    private static string FormatFiles(long count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "file" : "files")}");
}
