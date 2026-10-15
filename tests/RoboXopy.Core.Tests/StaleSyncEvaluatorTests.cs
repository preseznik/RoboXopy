using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class StaleSyncEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static SyncProfile Profile() => new()
    {
        CreatedAt = Now - TimeSpan.FromDays(10),
        Schedule = new ScheduleSettings(),
    };

    private static RunRecord Sync(TimeSpan age) => new()
    {
        DryRun = false,
        Outcome = RunOutcome.Success,
        StartedAt = Now - age - TimeSpan.FromMinutes(1),
        EndedAt = Now - age,
    };

    private static RunRecord ScheduledPreview(TimeSpan age, long copied = 0, long extras = 0, long mismatch = 0) => new()
    {
        DryRun = true,
        Trigger = RunTrigger.Scheduled,
        Outcome = RunOutcome.Success,
        StartedAt = Now - age - TimeSpan.FromMinutes(1),
        EndedAt = Now - age,
        Summary = new RobocopySummary
        {
            Files = new SummaryRow { Copied = copied, Extras = extras, Mismatch = mismatch },
        },
    };

    [Fact]
    public void NormalProfilesAreAmberAfterOneDayAndRedAfterSeven()
    {
        var profile = Profile();

        Assert.Equal(StaleSyncLevel.Fresh, StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromHours(23))], Now).Level);
        Assert.Equal(StaleSyncLevel.Stale, StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromHours(24))], Now).Level);
        Assert.Equal(StaleSyncLevel.Overdue, StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromDays(7))], Now).Level);
    }

    [Fact]
    public void IntervalProfilesAreAmberAfterOneMissAndRedAfterThree()
    {
        var profile = Profile();
        profile.Schedule.Enabled = true;
        profile.Schedule.Kind = ScheduleKind.Interval;
        profile.Schedule.IntervalUnit = IntervalUnit.Hours;
        profile.Schedule.IntervalValue = 2;

        Assert.Equal(StaleSyncLevel.Stale, StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromHours(2))], Now).Level);
        Assert.Equal(StaleSyncLevel.Overdue, StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromHours(6))], Now).Level);
    }

    [Fact]
    public void ScheduledPreviewWithChangesMarksTheProfileStaleUntilItIsSynced()
    {
        var profile = Profile();
        var stale = StaleSyncEvaluator.Evaluate(profile, [ScheduledPreview(TimeSpan.FromHours(1), copied: 3), Sync(TimeSpan.FromHours(2))], Now);

        Assert.Equal(StaleSyncLevel.Stale, stale.Level);
        Assert.Contains("Scheduled preview found", stale.Message);

        var fresh = StaleSyncEvaluator.Evaluate(profile, [Sync(TimeSpan.FromMinutes(30)), ScheduledPreview(TimeSpan.FromHours(1), copied: 3)], Now);
        Assert.Equal(StaleSyncLevel.Fresh, fresh.Level);
    }

    [Fact]
    public void CleanPreviewOnlyScheduleRefreshesTheVerificationTime()
    {
        var profile = Profile();
        profile.Schedule.Enabled = true;
        profile.Schedule.PreviewOnly = true;

        var status = StaleSyncEvaluator.Evaluate(profile, [ScheduledPreview(TimeSpan.FromHours(1)), Sync(TimeSpan.FromDays(8))], Now);

        Assert.Equal(StaleSyncLevel.Fresh, status.Level);
    }
}
