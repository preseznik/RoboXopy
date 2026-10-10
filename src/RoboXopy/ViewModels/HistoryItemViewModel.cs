using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>One row in a profile's run history.</summary>
public sealed partial class HistoryItemViewModel(RunRecord record) : ObservableObject
{
    /// <summary>Briefly set when the row is picked from the activity chart.</summary>
    [ObservableProperty] private bool _isHighlighted;

    public RunRecord Record { get; } = record;
    public bool HasFindings => Record.Findings is { Count: > 0 };
    public bool HasWarningFinding => Record.HasWarnings;
    public string? FindingsText => HasFindings ? string.Join(Environment.NewLine, Record.Findings!.Select(f => f.Message)) : null;
    public RunOutcome Outcome => Record.Outcome;
    public bool DryRun => Record.DryRun;
    public string Title => RunFormatting.OutcomeTitle(Record.Outcome, Record.DryRun);
    public string WhenText => ScheduleDescriber.FormatRelative(Record.StartedAt.LocalDateTime, DateTime.Now);
    public string WhenFull => Record.StartedAt.LocalDateTime.ToString("F");
    public string DurationText => RunFormatting.FormatDuration(Record.Duration);
    public string TriggerText => RunFormatting.TriggerText(Record.Trigger);
    public string SummaryText => RunFormatting.SummaryText(Record.Summary, Record.DryRun, Record.PurgesExtras);
    public string? Message => Record.Message;
    public bool HasLog => !string.IsNullOrEmpty(Record.LogFile) && File.Exists(Record.LogFile);

    public string DetailText
    {
        get
        {
            var parts = new List<string> { TriggerText, DurationText };
            if (Record.DryRun) parts.Insert(0, "Preview");
            if (Record.ExitCode >= 0) parts.Add($"exit code {Record.ExitCode}");
            return string.Join(" · ", parts);
        }
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (HasLog) Shell.Open(Record.LogFile!);
    }

    [RelayCommand]
    private void ShowLogInFolder()
    {
        if (HasLog) Shell.Reveal(Record.LogFile!);
    }
}

internal static class Shell
{
    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
        }
    }

    public static void Reveal(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
