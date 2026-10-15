using RoboXopy.Core.Execution;
using RoboXopy.Core.Models;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Tests;

public sealed class ScheduledRunLimiterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-limit-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public ScheduledRunLimiterTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task DefaultSettingSerializesScheduledRuns()
    {
        var settings = new AppSettings();
        using var first = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        var waiting = ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);

        await Task.Delay(150);
        Assert.False(waiting.IsCompleted);

        first.Dispose();
        using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task OverrideAllowsConfiguredNumberOfRuns()
    {
        var settings = new AppSettings { SimultaneousScheduledRuns = true, MaxSimultaneousScheduledRuns = 2 };
        using var first = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        using var second = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        var waiting = ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);

        await Task.Delay(150);
        Assert.False(waiting.IsCompleted);

        first.Dispose();
        using var third = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CapsConcurrentRunSettingAtThree()
    {
        var settings = new AppSettings { SimultaneousScheduledRuns = true, MaxSimultaneousScheduledRuns = 99 };

        Assert.Equal(3, settings.MaxSimultaneousScheduledRuns);
        Assert.Equal(3, ScheduledRunLimiter.Limit(settings));

        using var first = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        using var second = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        using var third = await ScheduledRunLimiter.AcquireAsync(_paths, settings, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = ScheduledRunLimiter.AcquireAsync(_paths, settings, cancellation.Token);

        await Task.Delay(150);
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
