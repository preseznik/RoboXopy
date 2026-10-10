using System.Diagnostics;

namespace RoboXopy.Infrastructure;

/// <summary>
/// Lets <c>RoboXopy --exit</c> (run by the uninstaller) ask other RoboXopy processes started from the same
/// executable to quit cleanly. Every process listens on an event named after its own process id.
/// </summary>
internal sealed class ExitRequest : IDisposable
{
    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _registration;

    private ExitRequest(Action onExitRequested)
    {
        _event = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(Environment.ProcessId));
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onExitRequested(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Starts listening; <paramref name="onExitRequested"/> runs once, on a thread-pool thread.</summary>
    public static ExitRequest Listen(Action onExitRequested) => new(onExitRequested);

    public void Dispose()
    {
        _registration.Unregister(null);
        _event.Dispose();
    }

    /// <summary>How many other processes are running this executable (other windows or command-line runs).</summary>
    public static int CountOthers()
    {
        var others = FindOthers();
        foreach (var process in others) process.Dispose();
        return others.Count;
    }

    /// <summary>Asks every other process running this executable to exit and waits for them to do so.</summary>
    /// <returns>How many processes were running, and how many were still running when the timeout expired.</returns>
    public static (int Found, int Remaining) SignalOthers(TimeSpan timeout)
    {
        var others = FindOthers();

        foreach (var process in others)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName(process.Id), out var handle))
                {
                    using (handle) handle.Set();
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Belongs to a process we may not signal; it is reported as still running below.
            }
        }

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var remaining = 0;
        foreach (var process in others)
        {
            using (process)
            {
                if (!process.WaitForExit((int)Math.Max(0, deadline - Environment.TickCount64))) remaining++;
            }
        }

        return (others.Count, remaining);
    }

    private static List<Process> FindOthers()
    {
        var others = new List<Process>();
        var exePath = Environment.ProcessPath;
        if (exePath is null) return others;

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            if (process.Id != Environment.ProcessId && IsSameExecutable(process, exePath)) others.Add(process);
            else process.Dispose();
        }

        return others;
    }

    private static bool IsSameExecutable(Process process, string exePath)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Another user's or an elevated process, or one that has already exited.
            return false;
        }
    }

    private static string EventName(int processId) => $@"Local\RoboXopy.Exit.{processId}";
}
