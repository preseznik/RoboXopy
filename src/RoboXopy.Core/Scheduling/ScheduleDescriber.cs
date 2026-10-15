using System.Globalization;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Scheduling;

/// <summary>Produces short human-readable descriptions of schedules and modes.</summary>
public static class ScheduleDescriber
{
    public static string Describe(ScheduleSettings schedule, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!schedule.Enabled) return "Manual";

        var time = FormatTime(schedule.TimeOfDay, culture);
        var description = schedule.Kind switch
        {
            ScheduleKind.Interval => DescribeInterval(schedule),
            ScheduleKind.Daily => $"Daily at {time}",
            ScheduleKind.Weekly => DescribeWeekly(schedule, time, culture),
            ScheduleKind.AtLogon => "At sign-in",
            _ => "Scheduled",
        };
        return schedule.PreviewOnly ? "Preview " + description.ToLowerInvariant() : description;
    }

    public static string DescribeMode(CopyMode mode) => mode switch
    {
        CopyMode.Mirror => "Mirror",
        CopyMode.MoveFiles => "Move files",
        CopyMode.MoveAll => "Move all",
        _ => "Copy",
    };

    public static string FormatTime(TimeSpan time, CultureInfo culture) =>
        DateTime.Today.Add(time).ToString(culture.DateTimeFormat.ShortTimePattern, culture);

    public static string FormatRelative(DateTime when, DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var time = when.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        if (when.Date == now.Date) return $"Today {time}";
        if (when.Date == now.Date.AddDays(1)) return $"Tomorrow {time}";
        if (when.Date == now.Date.AddDays(-1)) return $"Yesterday {time}";
        if (Math.Abs((when.Date - now.Date).TotalDays) < 7)
        {
            return $"{culture.DateTimeFormat.GetAbbreviatedDayName(when.DayOfWeek)} {time}";
        }

        return when.ToString("g", culture);
    }

    private static string DescribeInterval(ScheduleSettings schedule)
    {
        var n = Math.Max(1, schedule.IntervalValue);
        return schedule.IntervalUnit == IntervalUnit.Hours
            ? n == 1 ? "Every hour" : $"Every {n} hours"
            : n == 1 ? "Every minute" : $"Every {n} minutes";
    }

    private static string DescribeWeekly(ScheduleSettings schedule, string time, CultureInfo culture)
    {
        var days = schedule.SelectedDays.ToList();
        if (days.Count == 0) return "Weekly (no days selected)";
        if (days.Count == 7) return $"Every day at {time}";

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (days.Count == 5 && weekdays.All(days.Contains)) return $"Weekdays at {time}";
        if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday)) return $"Weekends at {time}";

        return $"{string.Join(", ", days.Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d)))} at {time}";
    }
}
