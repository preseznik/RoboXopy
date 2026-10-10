using System.Security.Cryptography;
using System.Text;

namespace RoboXopy.Infrastructure;

/// <summary>
/// Ensures a single GUI instance per user and data folder. A second launch signals the first
/// instance to show its window and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private RegisteredWaitHandle? _registration;

    public SingleInstance(string dataRoot)
    {
        var suffix = Suffix(dataRoot);
        _mutex = new Mutex(initiallyOwned: true, MutexName(suffix), out var createdNew);
        IsFirstInstance = createdNew;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\RoboXopy.Activate.{suffix}");
    }

    public bool IsFirstInstance { get; }

    /// <summary>True when a RoboXopy window process is running for the given data folder.</summary>
    public static bool IsGuiRunning(string dataRoot)
    {
        if (Mutex.TryOpenExisting(MutexName(Suffix(dataRoot)), out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }

    public void ActivateFirstInstance() => _activateEvent.Set();

    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        _mutex.Dispose();
        _activateEvent.Dispose();
    }

    private static string MutexName(string suffix) => $@"Local\RoboXopy.App.{suffix}";

    private static string Suffix(string dataRoot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataRoot).TrimEnd('\\').ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 6);
    }
}
