using RoboXopy.Core.Models;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Execution;

/// <summary>
/// Coordinates scheduled runs across RoboXopy processes. File slots are released automatically
/// when their owning process exits, so Task Scheduler and the in-app scheduler share one limit.
/// </summary>
public static class ScheduledRunLimiter
{
    private const int MaximumSlots = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public static int Limit(AppSettings settings) =>
        settings.SimultaneousScheduledRuns
            ? Math.Clamp(settings.MaxSimultaneousScheduledRuns, 1, MaximumSlots)
            : 1;

    public static async Task<IDisposable> AcquireAsync(AppPaths paths, AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(paths.LocksDirectory, "scheduled");
        Directory.CreateDirectory(directory);
        var limit = Limit(settings);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var slot = 1; slot <= limit; slot++)
            {
                var path = Path.Combine(directory, $"slot-{slot}.lock");
                try
                {
                    return new Lease(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
                }
                catch (IOException)
                {
                    // The slot belongs to another scheduled run, or its DeleteOnClose cleanup is racing
                    // with this acquisition attempt. Try the next slot or wait and retry.
                }
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Lease(FileStream stream) : IDisposable
    {
        private FileStream? _stream = stream;

        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    }
}
