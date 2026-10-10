using System.Globalization;
using System.Xml.Linq;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Tests;

public class ScheduleTests
{
    private static ScheduleSettings Schedule(ScheduleKind kind) => new()
    {
        Enabled = true,
        Kind = kind,
        StartDate = new DateTime(2026, 1, 1),
        TimeOfDay = new TimeSpan(2, 0, 0),
    };

    [Fact]
    public void Disabled_HasNoOccurrence()
    {
        var s = Schedule(ScheduleKind.Daily);
        s.Enabled = false;
        Assert.Null(ScheduleCalculator.NextOccurrence(s, new DateTime(2026, 3, 1)));
    }

    [Fact]
    public void Daily_NextOccurrence()
    {
        var s = Schedule(ScheduleKind.Daily);
        Assert.Equal(new DateTime(2026, 3, 1, 2, 0, 0), ScheduleCalculator.NextOccurrence(s, new DateTime(2026, 3, 1, 1, 0, 0)));
        Assert.Equal(new DateTime(2026, 3, 2, 2, 0, 0), ScheduleCalculator.NextOccurrence(s, new DateTime(2026, 3, 1, 2, 0, 0)));
        Assert.Equal(new DateTime(2026, 1, 1, 2, 0, 0), ScheduleCalculator.NextOccurrence(s, new DateTime(2025, 6, 1)));
    }

    [Fact]
    public void Weekly_SkipsUnselectedDays()
    {
        var s = Schedule(ScheduleKind.Weekly);
        // Monday–Friday selected by default. 2026-03-06 is a Friday.
        var afterFriday = new DateTime(2026, 3, 6, 3, 0, 0);
        Assert.Equal(new DateTime(2026, 3, 9, 2, 0, 0), ScheduleCalculator.NextOccurrence(s, afterFriday));

        s.Monday = s.Tuesday = s.Wednesday = s.Thursday = s.Friday = false;
        Assert.Null(ScheduleCalculator.NextOccurrence(s, afterFriday));

        s.Sunday = true;
        Assert.Equal(new DateTime(2026, 3, 8, 2, 0, 0), ScheduleCalculator.NextOccurrence(s, afterFriday));
    }

    [Fact]
    public void Interval_IsAnchoredToStart()
    {
        var s = Schedule(ScheduleKind.Interval);
        s.IntervalUnit = IntervalUnit.Minutes;
        s.IntervalValue = 45;

        Assert.Equal(new DateTime(2026, 1, 1, 2, 45, 0), ScheduleCalculator.NextOccurrence(s, new DateTime(2026, 1, 1, 2, 0, 0)));
        Assert.Equal(new DateTime(2026, 1, 1, 3, 30, 0), ScheduleCalculator.NextOccurrence(s, new DateTime(2026, 1, 1, 3, 10, 0)));
    }

    [Fact]
    public void AtLogon_HasNoTimeOccurrence()
    {
        Assert.Null(ScheduleCalculator.NextOccurrence(Schedule(ScheduleKind.AtLogon), DateTime.Now));
    }

    [Fact]
    public void IsDue_DetectsOccurrenceInWindow()
    {
        var s = Schedule(ScheduleKind.Daily);
        Assert.True(ScheduleCalculator.IsDue(s, new DateTime(2026, 3, 1, 1, 59, 50), new DateTime(2026, 3, 1, 2, 0, 5)));
        Assert.False(ScheduleCalculator.IsDue(s, new DateTime(2026, 3, 1, 2, 0, 5), new DateTime(2026, 3, 1, 2, 0, 25)));
    }

    [Fact]
    public void Describes()
    {
        var c = CultureInfo.GetCultureInfo("en-GB");
        var s = Schedule(ScheduleKind.Daily);
        Assert.Equal("Daily at 02:00", ScheduleDescriber.Describe(s, c));

        s.Kind = ScheduleKind.Weekly;
        Assert.Equal("Weekdays at 02:00", ScheduleDescriber.Describe(s, c));
        s.Monday = s.Tuesday = s.Thursday = false;
        Assert.Equal("Wed, Fri at 02:00", ScheduleDescriber.Describe(s, c));

        s.Kind = ScheduleKind.Interval;
        s.IntervalValue = 15;
        s.IntervalUnit = IntervalUnit.Minutes;
        Assert.Equal("Every 15 minutes", ScheduleDescriber.Describe(s, c));

        s.Enabled = false;
        Assert.Equal("Manual", ScheduleDescriber.Describe(s, c));
    }
}

public class TaskSchedulerXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static SyncProfile Profile(ScheduleKind kind) => new()
    {
        Name = "Docs: nightly/backup",
        Source = @"C:\Docs",
        Destination = @"D:\Docs",
        Schedule = new ScheduleSettings
        {
            Enabled = true,
            Runner = ScheduleRunner.TaskScheduler,
            Kind = kind,
            StartDate = new DateTime(2026, 1, 1),
            TimeOfDay = new TimeSpan(22, 30, 0),
            IntervalValue = 2,
            IntervalUnit = IntervalUnit.Hours,
        },
    };

    [Fact]
    public void TaskName_IsSanitizedAndUnique()
    {
        var p = Profile(ScheduleKind.Daily);
        Assert.Equal($@"\RoboXopy\Docs_ nightly_backup [{p.ShortId}]", TaskSchedulerService.TaskNameFor(p));
    }

    [Fact]
    public void Daily_UsesCalendarTrigger()
    {
        var p = Profile(ScheduleKind.Daily);
        var doc = XDocument.Parse(TaskSchedulerService.BuildTaskXml(p, @"C:\Apps\RoboXopy.exe", @"PC\user"));

        var trigger = doc.Descendants(Ns + "CalendarTrigger").Single();
        Assert.Equal("2026-01-01T22:30:00", trigger.Element(Ns + "StartBoundary")!.Value);
        Assert.NotNull(trigger.Element(Ns + "ScheduleByDay"));
        Assert.Equal(@"C:\Apps\RoboXopy.exe", doc.Descendants(Ns + "Command").Single().Value);
        Assert.Equal($"--run {p.Id} --trigger task", doc.Descendants(Ns + "Arguments").Single().Value);
        Assert.Equal(@"PC\user", doc.Descendants(Ns + "UserId").Single().Value);
        Assert.Equal("IgnoreNew", doc.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);
    }

    [Fact]
    public void CustomDataFolder_IsPassedToTheTask()
    {
        var p = Profile(ScheduleKind.Daily);
        var paths = new AppPaths(@"D:\Portable\RoboXopy Data\");
        var doc = XDocument.Parse(TaskSchedulerService.BuildTaskXml(p, @"C:\RoboXopy.exe", @"PC\user", paths.LaunchArguments));
        Assert.Equal($"--run {p.Id} --trigger task --data-dir \"D:\\Portable\\RoboXopy Data\"", doc.Descendants(Ns + "Arguments").Single().Value);
    }

    [Fact]
    public void LaunchArguments_EmptyForStandardFolder_AndSafeForDriveRoots()
    {
        Assert.Equal("", new AppPaths(AppPaths.StandardRoot + "\\").LaunchArguments);
        Assert.Equal(" --data-dir \"E:\\\\\"", new AppPaths(@"E:\").LaunchArguments);
    }

    [Fact]
    public void Weekly_ListsSelectedDays()
    {
        var p = Profile(ScheduleKind.Weekly);
        p.Schedule.Saturday = true;
        var doc = XDocument.Parse(TaskSchedulerService.BuildTaskXml(p, @"C:\RoboXopy.exe", @"PC\user"));
        var days = doc.Descendants(Ns + "DaysOfWeek").Single().Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"], days);
    }

    [Fact]
    public void Interval_UsesRepetition()
    {
        var p = Profile(ScheduleKind.Interval);
        var doc = XDocument.Parse(TaskSchedulerService.BuildTaskXml(p, @"C:\RoboXopy.exe", @"PC\user"));
        Assert.Equal("PT2H", doc.Descendants(Ns + "Interval").Single().Value);
    }

    [Fact]
    public void Logon_UsesLogonTrigger()
    {
        var p = Profile(ScheduleKind.AtLogon);
        var doc = XDocument.Parse(TaskSchedulerService.BuildTaskXml(p, @"C:\RoboXopy.exe", @"PC\user"));
        Assert.Single(doc.Descendants(Ns + "LogonTrigger"));
    }

    [Theory]
    [InlineData(@"C:\Apps\RoboXopy.exe", true)]
    [InlineData(@"c:\apps\ROBOXOPY.EXE", true)]
    [InlineData(@"C:\Apps\..\Apps\RoboXopy.exe", true)]
    [InlineData(@"C:\Other\RoboXopy.exe", false)]
    public void TaskRunsExecutable_MatchesTheTaskCommand(string exePath, bool expected)
    {
        var xml = TaskSchedulerService.BuildTaskXml(Profile(ScheduleKind.Daily), @"C:\Apps\RoboXopy.exe", @"PC\user");
        Assert.Equal(expected, TaskSchedulerService.TaskRunsExecutable(xml, exePath));
    }

    [Fact]
    public void TaskRunsExecutable_HandlesQuotesAndEnvironmentVariables()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\RoboXopy\RoboXopy.exe");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Actions Context="Author">
                <Exec><Command>"%LOCALAPPDATA%\Programs\RoboXopy\RoboXopy.exe"</Command></Exec>
              </Actions>
            </Task>
            """;
        Assert.True(TaskSchedulerService.TaskRunsExecutable(xml, exe));
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Actions><ComHandler /></Actions></Task>")]
    public void TaskRunsExecutable_FalseForUnrelatedOrInvalidTasks(string xml) =>
        Assert.False(TaskSchedulerService.TaskRunsExecutable(xml, @"C:\Apps\RoboXopy.exe"));

    [Theory]
    [InlineData(15, "PT15M")]
    [InlineData(120, "PT2H")]
    [InlineData(1440, "P1D")]
    [InlineData(0, "PT1M")]
    public void IsoDuration(int minutes, string expected) =>
        Assert.Equal(expected, TaskSchedulerService.ToIsoDuration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void RegistrationCurrent_TracksScheduleChanges()
    {
        var p = Profile(ScheduleKind.Daily);
        p.RegisteredTaskName = TaskSchedulerService.TaskNameFor(p);
        p.RegisteredTaskExePath = @"C:\RoboXopy.exe";
        p.RegisteredTaskFingerprint = p.Schedule.Fingerprint();
        Assert.True(TaskSchedulerService.IsRegistrationCurrent(p, @"C:\RoboXopy.exe"));

        p.Schedule.TimeOfDay = new TimeSpan(23, 0, 0);
        Assert.False(TaskSchedulerService.IsRegistrationCurrent(p, @"C:\RoboXopy.exe"));
    }
}
