using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoboXopy.Core.Models;

public sealed partial class ScheduleSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    /// <summary>Runs the scheduled operation as a dry-run and uses its result to report pending changes.</summary>
    [ObservableProperty] private bool _previewOnly;
    [ObservableProperty] private ScheduleKind _kind = ScheduleKind.Daily;
    [ObservableProperty] private ScheduleRunner _runner = ScheduleRunner.App;

    [ObservableProperty] private int _intervalValue = 1;
    [ObservableProperty] private IntervalUnit _intervalUnit = IntervalUnit.Hours;

    [ObservableProperty] private TimeSpan _timeOfDay = new(2, 0, 0);
    [ObservableProperty] private DateTime _startDate = DateTime.Today;

    [ObservableProperty] private bool _monday = true;
    [ObservableProperty] private bool _tuesday = true;
    [ObservableProperty] private bool _wednesday = true;
    [ObservableProperty] private bool _thursday = true;
    [ObservableProperty] private bool _friday = true;
    [ObservableProperty] private bool _saturday;
    [ObservableProperty] private bool _sunday;

    [JsonIgnore]
    public TimeSpan Interval =>
        IntervalUnit == IntervalUnit.Hours
            ? TimeSpan.FromHours(Math.Max(1, IntervalValue))
            : TimeSpan.FromMinutes(Math.Max(1, IntervalValue));

    public bool IsDaySelected(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => false,
    };

    [JsonIgnore]
    public IEnumerable<DayOfWeek> SelectedDays =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Where(IsDaySelected);

    /// <summary>A stable fingerprint of everything that affects a Task Scheduler registration.</summary>
    public string Fingerprint() =>
        string.Join('|', Enabled, PreviewOnly, Kind, Runner, IntervalValue, IntervalUnit, TimeOfDay, StartDate.Date.ToString("yyyy-MM-dd"),
            Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday);
}
