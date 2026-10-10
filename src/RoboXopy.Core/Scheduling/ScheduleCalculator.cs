using RoboXopy.Core.Models;

namespace RoboXopy.Core.Scheduling;

/// <summary>Computes schedule occurrences in local time.</summary>
public static class ScheduleCalculator
{
    /// <summary>
    /// Returns the first occurrence strictly after <paramref name="after"/>, or null when the schedule
    /// has no time-based occurrences (disabled, sign-in trigger, or no weekdays selected).
    /// </summary>
    public static DateTime? NextOccurrence(ScheduleSettings schedule, DateTime after)
    {
        if (!schedule.Enabled) return null;

        var start = schedule.StartDate.Date;
        var time = Clamp(schedule.TimeOfDay);

        switch (schedule.Kind)
        {
            case ScheduleKind.Interval:
            {
                var anchor = start + time;
                if (after < anchor) return anchor;
                var interval = schedule.Interval;
                var steps = (after - anchor).Ticks / interval.Ticks + 1;
                return anchor + TimeSpan.FromTicks(steps * interval.Ticks);
            }

            case ScheduleKind.Daily:
            {
                var day = after.Date < start ? start : after.Date;
                var candidate = day + time;
                return candidate > after ? candidate : candidate.AddDays(1);
            }

            case ScheduleKind.Weekly:
            {
                var day = after.Date < start ? start : after.Date;
                for (var i = 0; i < 8; i++)
                {
                    var candidate = day.AddDays(i) + time;
                    if (candidate > after && schedule.IsDaySelected(candidate.DayOfWeek)) return candidate;
                }

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>True when at least one occurrence falls in (<paramref name="fromExclusive"/>, <paramref name="toInclusive"/>].</summary>
    public static bool IsDue(ScheduleSettings schedule, DateTime fromExclusive, DateTime toInclusive) =>
        NextOccurrence(schedule, fromExclusive) is { } next && next <= toInclusive;

    private static TimeSpan Clamp(TimeSpan time) =>
        time < TimeSpan.Zero ? TimeSpan.Zero : time >= TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1) : time;
}
