using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Controls;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>Read-only inspection state reconstructed from a completed run's retained log.</summary>
public sealed partial class HistoryRunDetailViewModel : ObservableObject
{
    private const int MaxEntriesPerKind = 50_000;

    private readonly List<OutputEntry> _allEntries = [];
    private readonly Dictionary<BreakdownKind, List<OutputEntry>> _entriesByKind = new()
    {
        [BreakdownKind.Unchanged] = [],
        [BreakdownKind.Copied] = [],
        [BreakdownKind.Removed] = [],
        [BreakdownKind.Kept] = [],
    };

    public HistoryRunDetailViewModel(RunRecord record)
    {
        Record = record;
        Breakdown = RunSessionViewModel.BuildBreakdown(record);
    }

    public RunRecord Record { get; }
    public IReadOnlyList<BreakdownSegment>? Breakdown { get; }
    public static IReadOnlyList<OutputTypeFilter> OutputTypeFilters => RunSessionViewModel.OutputTypeFilters;
    public bool HasLog => !string.IsNullOrEmpty(Record.LogFile) && File.Exists(Record.LogFile);
    public string Title => $"{Record.ProfileName} — {RunFormatting.OutcomeTitle(Record.Outcome, Record.DryRun)}";
    public string DetailText => $"{Record.StartedAt.LocalDateTime:F} · {RunFormatting.FormatDuration(Record.Duration)}";
    public string SummaryText => RunFormatting.SummaryText(Record.Summary, Record.DryRun, Record.PurgesExtras);

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private BreakdownKind? _selectedBreakdownKind;
    [ObservableProperty] private IReadOnlyList<OutputEntry> _filteredEntries = [];
    [ObservableProperty] private string _outputNameFilter = "";
    [ObservableProperty] private OutputTypeFilter _selectedOutputTypeFilter = OutputTypeFilters[0];
    [ObservableProperty] private OutputSortColumn? _selectedOutputSortColumn;
    [ObservableProperty] private bool _isOutputSortDescending;
    [ObservableProperty] private long _filteredEntriesDisplayCount;
    [ObservableProperty] private long _filteredEntriesByteCount;

    public bool HasBreakdown => Breakdown is { Count: > 0 };
    public bool IsBreakdownFilterActive => SelectedBreakdownKind is not null;
    public Brush? OutputListBackground => SelectedBreakdownKind switch
    {
        BreakdownKind.Unchanged => new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
        BreakdownKind.Copied => new SolidColorBrush(Color.FromArgb(28, 59, 142, 219)),
        BreakdownKind.Removed => new SolidColorBrush(Color.FromArgb(30, 226, 101, 77)),
        BreakdownKind.Kept => new SolidColorBrush(Color.FromArgb(25, 128, 128, 128)),
        _ => null,
    };
    public string FilteredEntriesBytesText => RunFormatting.FormatBytes(FilteredEntriesByteCount);
    public string OutputSortDirectionIcon => IsOutputSortDescending ? "↓" : "↑";
    public string OutputSortDirectionToolTip => IsOutputSortDescending ? "Descending; click for ascending" : "Ascending; click for descending";

    partial void OnSelectedBreakdownKindChanged(BreakdownKind? value)
    {
        OnPropertyChanged(nameof(IsBreakdownFilterActive));
        OnPropertyChanged(nameof(OutputListBackground));
        RefreshFilteredEntries();
    }

    partial void OnOutputNameFilterChanged(string value) => RefreshFilteredEntries();
    partial void OnSelectedOutputTypeFilterChanged(OutputTypeFilter value) => RefreshFilteredEntries();
    partial void OnFilteredEntriesByteCountChanged(long value) => OnPropertyChanged(nameof(FilteredEntriesBytesText));
    partial void OnIsOutputSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(OutputSortDirectionIcon));
        OnPropertyChanged(nameof(OutputSortDirectionToolTip));
    }

    public async Task LoadAsync()
    {
        if (!HasLog)
        {
            LoadError = "The log file for this run is no longer available.";
            IsLoading = false;
            return;
        }

        try
        {
            var entries = await Task.Run(ReadEntries);
            _allEntries.AddRange(entries.All);
            foreach (var (kind, kindEntries) in entries.ByKind)
            {
                _entriesByKind[kind].AddRange(kindEntries);
            }

            RefreshFilteredEntries();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadError = $"Could not read the saved log: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleBreakdownFilter(BreakdownKind kind)
    {
        if (kind is not (BreakdownKind.Unchanged or BreakdownKind.Copied or BreakdownKind.Removed or BreakdownKind.Kept)) return;
        SelectedBreakdownKind = SelectedBreakdownKind == kind ? null : kind;
    }

    [RelayCommand]
    private void SortOutput(OutputSortColumn column)
    {
        SelectedOutputSortColumn = column;
        RefreshFilteredEntries();
    }

    [RelayCommand]
    private void ToggleOutputSortDirection()
    {
        IsOutputSortDescending = !IsOutputSortDescending;
        RefreshFilteredEntries();
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (HasLog) Shell.Open(Record.LogFile!);
    }

    [RelayCommand]
    private void RevealOutputEntry(OutputEntry? entry)
    {
        if (entry?.CanReveal == true) Shell.Reveal(entry.Name!);
    }

    private SavedOutput ReadEntries()
    {
        var output = new List<OutputEntry>();
        var byKind = new Dictionary<BreakdownKind, List<OutputEntry>>
        {
            [BreakdownKind.Unchanged] = [],
            [BreakdownKind.Copied] = [],
            [BreakdownKind.Removed] = [],
            [BreakdownKind.Kept] = [],
        };

        foreach (var text in File.ReadLines(Record.LogFile!))
        {
            var line = RobocopyOutputParser.Parse(text);
            if (line.Kind is not (OutputLineKind.File or OutputLineKind.Directory)) continue;
            var entry = new OutputEntry(line);
            if (output.Count < MaxEntriesPerKind) output.Add(entry);
            if (GetBreakdownKind(entry) is { } kind && byKind[kind].Count < MaxEntriesPerKind)
            {
                byKind[kind].Add(entry);
            }
        }
        return new SavedOutput(output, byKind);
    }

    private void RefreshFilteredEntries()
    {
        IReadOnlyList<OutputEntry> source = SelectedBreakdownKind is { } kind
            ? _entriesByKind[kind]
            : _allEntries;

        IEnumerable<OutputEntry> entries = source;
        if (SelectedOutputTypeFilter.Type is { } type) entries = entries.Where(entry => entry.AdditionType == type);
        if (!string.IsNullOrWhiteSpace(OutputNameFilter))
        {
            entries = entries.Where(entry => entry.Name?.Contains(OutputNameFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        var displayed = SortEntries(entries).ToList();
        FilteredEntries = displayed;
        FilteredEntriesDisplayCount = displayed.Count;
        FilteredEntriesByteCount = displayed.Sum(entry => entry.Size ?? 0);
    }

    private IEnumerable<OutputEntry> SortEntries(IEnumerable<OutputEntry> entries) => SelectedOutputSortColumn switch
    {
        OutputSortColumn.Name when IsOutputSortDescending => entries.OrderByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        OutputSortColumn.Name => entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        OutputSortColumn.Size when IsOutputSortDescending => entries.OrderByDescending(entry => entry.Size ?? -1),
        OutputSortColumn.Size => entries.OrderBy(entry => entry.Size ?? -1),
        OutputSortColumn.AdditionType when IsOutputSortDescending => entries.OrderByDescending(entry => entry.AdditionType),
        OutputSortColumn.AdditionType => entries.OrderBy(entry => entry.AdditionType),
        _ => entries,
    };

    private BreakdownKind? GetBreakdownKind(OutputEntry entry)
    {
        if (entry.Kind == OutputLineKind.File && IsUnchanged(entry.Status)) return BreakdownKind.Unchanged;
        if (entry.IsExtra) return Record.PurgesExtras ? BreakdownKind.Removed : BreakdownKind.Kept;
        return entry.Kind == OutputLineKind.File ? BreakdownKind.Copied : null;
    }

    private static bool IsUnchanged(string? status) =>
        status is not null && status.Trim().Equals("same", StringComparison.OrdinalIgnoreCase);

    private sealed record SavedOutput(
        IReadOnlyList<OutputEntry> All,
        IReadOnlyDictionary<BreakdownKind, List<OutputEntry>> ByKind);
}
