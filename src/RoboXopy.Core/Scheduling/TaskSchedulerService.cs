using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Scheduling;

public sealed record SchtasksResult(bool Success, string Output);

/// <summary>Registers profiles as Windows Task Scheduler tasks (via schtasks.exe and task XML).</summary>
/// <param name="launchArguments">Extra arguments appended to every task's command line (see <see cref="Storage.AppPaths.LaunchArguments"/>).</param>
public sealed class TaskSchedulerService(string launchArguments = "")
{
    public const string TaskFolder = @"\RoboXopy\";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string TaskNameFor(SyncProfile profile)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', '[', ']']).ToHashSet();
        var name = new string(profile.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (name.Length == 0) name = "Profile";
        if (name.Length > 80) name = name[..80].TrimEnd();
        return $"{TaskFolder}{name} [{profile.ShortId}]";
    }

    public static string CurrentUserId => $"{Environment.UserDomainName}\\{Environment.UserName}";

    /// <summary>True when the registered task matches the profile's current schedule and executable.</summary>
    public static bool IsRegistrationCurrent(SyncProfile profile, string exePath) =>
        profile.RegisteredTaskName == TaskNameFor(profile)
        && string.Equals(profile.RegisteredTaskExePath, exePath, StringComparison.OrdinalIgnoreCase)
        && profile.RegisteredTaskFingerprint == profile.Schedule.Fingerprint();

    public static bool WantsTask(SyncProfile profile) =>
        profile.Schedule.Enabled && profile.Schedule.Runner == ScheduleRunner.TaskScheduler;

    public static string BuildTaskXml(SyncProfile profile, string exePath, string userId, string launchArguments = "")
    {
        var s = profile.Schedule;
        var start = (s.StartDate.Date + s.TimeOfDay).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        XElement trigger = s.Kind switch
        {
            ScheduleKind.Interval => new XElement(Ns + "TimeTrigger",
                new XElement(Ns + "Repetition",
                    new XElement(Ns + "Interval", ToIsoDuration(s.Interval)),
                    new XElement(Ns + "StopAtDurationEnd", "false")),
                new XElement(Ns + "StartBoundary", start),
                new XElement(Ns + "Enabled", "true")),
            ScheduleKind.Weekly => new XElement(Ns + "CalendarTrigger",
                new XElement(Ns + "StartBoundary", start),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "ScheduleByWeek",
                    new XElement(Ns + "DaysOfWeek", s.SelectedDays.Select(d => new XElement(Ns + d.ToString()))),
                    new XElement(Ns + "WeeksInterval", "1"))),
            ScheduleKind.AtLogon => new XElement(Ns + "LogonTrigger",
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "UserId", userId),
                new XElement(Ns + "Delay", "PT1M")),
            _ => new XElement(Ns + "CalendarTrigger",
                new XElement(Ns + "StartBoundary", start),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", "1"))),
        };

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Author", "RoboXopy"),
                    new XElement(Ns + "Description", $"Runs the RoboXopy profile \"{profile.Name}\" ({profile.Id})."),
                    new XElement(Ns + "URI", TaskNameFor(profile))),
                new XElement(Ns + "Triggers", trigger),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Ns + "Priority", "7")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", exePath),
                        new XElement(Ns + "Arguments", $"--run {profile.Id} --trigger task{launchArguments}"),
                        new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(exePath) ?? "")))));

        var sb = new StringBuilder();
        using (var writer = new Utf16StringWriter(sb))
        {
            doc.Save(writer);
        }

        return sb.ToString();
    }

    /// <summary>Creates or replaces the task for the profile and records the registration on the profile.</summary>
    public SchtasksResult Register(SyncProfile profile, string exePath)
    {
        var name = TaskNameFor(profile);
        if (profile.Schedule.Kind == ScheduleKind.Weekly && !profile.Schedule.SelectedDays.Any())
        {
            return new SchtasksResult(false, "Select at least one day of the week.");
        }

        if (profile.RegisteredTaskName is { Length: > 0 } previous && previous != name)
        {
            Delete(previous);
        }

        var file = Path.Combine(Path.GetTempPath(), $"roboxopy-task-{profile.ShortId}-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, BuildTaskXml(profile, exePath, CurrentUserId, launchArguments), Encoding.Unicode);
            var result = Schtasks("/Create", "/TN", name, "/XML", file, "/F");
            if (result.Success)
            {
                profile.RegisteredTaskName = name;
                profile.RegisteredTaskExePath = exePath;
                profile.RegisteredTaskFingerprint = profile.Schedule.Fingerprint();
            }

            return result;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>Removes the profile's task (if registered) and clears the registration.</summary>
    public SchtasksResult Unregister(SyncProfile profile)
    {
        if (string.IsNullOrEmpty(profile.RegisteredTaskName)) return new SchtasksResult(true, "");
        var result = Delete(profile.RegisteredTaskName);
        if (result.Success || !Exists(profile.RegisteredTaskName))
        {
            profile.RegisteredTaskName = null;
            profile.RegisteredTaskExePath = null;
            profile.RegisteredTaskFingerprint = null;
            return new SchtasksResult(true, result.Output);
        }

        return result;
    }

    /// <summary>Brings the Task Scheduler registration in line with the profile's schedule settings.</summary>
    public SchtasksResult? Sync(SyncProfile profile, string exePath)
    {
        if (WantsTask(profile))
        {
            if (IsRegistrationCurrent(profile, exePath) && Exists(profile.RegisteredTaskName!)) return null;
            return Register(profile, exePath);
        }

        return string.IsNullOrEmpty(profile.RegisteredTaskName) ? null : Unregister(profile);
    }

    public SchtasksResult Delete(string taskName) => Schtasks("/Delete", "/TN", taskName, "/F");

    public bool Exists(string taskName) => Schtasks("/Query", "/TN", taskName).Success;

    public SchtasksResult RunNow(string taskName) => Schtasks("/Run", "/TN", taskName);

    /// <summary>True when one of the task definition's actions runs <paramref name="exePath"/>.</summary>
    public static bool TaskRunsExecutable(string taskXml, string exePath)
    {
        try
        {
            var target = NormalizeCommandPath(exePath);
            return XDocument.Parse(taskXml)
                .Descendants(Ns + "Exec")
                .Select(exec => (string?)exec.Element(Ns + "Command"))
                .Any(command => !string.IsNullOrWhiteSpace(command)
                                && string.Equals(NormalizeCommandPath(command), target, StringComparison.OrdinalIgnoreCase));
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes every task in the RoboXopy task folder that runs <paramref name="exePath"/>, then the folder itself
    /// once it is empty. Used when RoboXopy is uninstalled, when the profiles that own the tasks may be gone.
    /// </summary>
    /// <returns>The names of the deleted tasks.</returns>
    public static IReadOnlyList<string> RemoveTasksFor(string exePath)
    {
        var removed = new List<string>();
        var serviceType = Type.GetTypeFromProgID("Schedule.Service");
        if (serviceType is null) return removed;

        try
        {
            // The COM API is used instead of schtasks.exe so task names and paths survive any console code page.
            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();

            dynamic folder;
            try
            {
                folder = service.GetFolder(TaskFolder.TrimEnd('\\'));
            }
            catch (COMException)
            {
                return removed; // No RoboXopy folder, so nothing was ever registered.
            }

            const int includeHidden = 1;
            var matches = new List<string>();
            foreach (var task in (IEnumerable)folder.GetTasks(includeHidden))
            {
                dynamic registered = task;
                if (TaskRunsExecutable((string)registered.Xml, exePath)) matches.Add((string)registered.Name);
            }

            foreach (var name in matches)
            {
                try
                {
                    folder.DeleteTask(name, 0);
                    removed.Add(name);
                }
                catch (COMException)
                {
                    // Left in place; Task Scheduler reports it as a task whose program is missing.
                }
            }

            if ((int)folder.GetTasks(includeHidden).Count == 0 && (int)folder.GetFolders(0).Count == 0)
            {
                service.GetFolder(@"\").DeleteFolder(TaskFolder.Trim('\\'), 0);
            }
        }
        catch (COMException)
        {
            // Task Scheduler is unavailable; there is nothing more an uninstaller can do about it.
        }

        return removed;
    }

    private static string NormalizeCommandPath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    public static void OpenTaskScheduler()
    {
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskschd.msc")) { UseShellExecute = true });
    }

    internal static string ToIsoDuration(TimeSpan interval)
    {
        var minutes = Math.Clamp((int)Math.Round(interval.TotalMinutes), 1, 31 * 24 * 60);
        if (minutes % (24 * 60) == 0) return $"P{minutes / (24 * 60)}D";
        if (minutes % 60 == 0) return $"PT{minutes / 60}H";
        return $"PT{minutes}M";
    }

    private static SchtasksResult Schtasks(params string[] arguments)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                return new SchtasksResult(false, "schtasks.exe timed out.");
            }

            var output = (stdout.Result + stderr.Result).Trim();
            return new SchtasksResult(process.ExitCode == 0, output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SchtasksResult(false, ex.Message);
        }
    }

    private sealed class Utf16StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.Unicode;
    }
}
