using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class NotificationFormattingTests
{
    [Fact]
    public void DetectsScheduledPreviewDelta()
    {
        var preview = new RunRecord
        {
            DryRun = true,
            Summary = new RobocopySummary
            {
                Files = new SummaryRow { Copied = 2, Extras = 1 },
            },
        };

        Assert.True(ScheduledPreviewAnalysis.HasDelta(preview));
        Assert.Contains("2 to copy", ScheduledPreviewAnalysis.Summary(preview));
        Assert.Contains("1 extra", ScheduledPreviewAnalysis.Summary(preview));
    }

    [Fact]
    public void IgnoresCleanOrNonPreviewRuns()
    {
        var cleanPreview = new RunRecord { DryRun = true, Summary = new RobocopySummary() };
        var run = new RunRecord
        {
            DryRun = false,
            Summary = new RobocopySummary { Files = new SummaryRow { Copied = 1 } },
        };

        Assert.False(ScheduledPreviewAnalysis.HasDelta(cleanPreview));
        Assert.False(ScheduledPreviewAnalysis.HasDelta(run));
    }
}
