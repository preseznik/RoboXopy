using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public sealed class ProfileRuleTesterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roboxopy-rules-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;

    public ProfileRuleTesterTests()
    {
        _source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private SyncProfile Profile() => new() { Source = _source, Destination = Path.Combine(_root, "destination") };

    [Fact]
    public void ExplainsIncludeAndExcludeFilePatterns()
    {
        var included = Path.Combine(_source, "report.txt");
        var excluded = Path.Combine(_source, "cache.tmp");
        File.WriteAllText(included, "report");
        File.WriteAllText(excluded, "cache");
        var profile = Profile();
        profile.Options.IncludeFiles.Add("*.txt");
        profile.Options.ExcludeFiles.Add("*.tmp");

        var includedResult = ProfileRuleTester.Test(profile, included);
        var excludedResult = ProfileRuleTester.Test(profile, excluded);

        Assert.Equal(RuleTestOutcome.Included, includedResult.Outcome);
        Assert.Contains(includedResult.Reasons, reason => reason.Contains("*.txt"));
        Assert.Equal(RuleTestOutcome.Excluded, excludedResult.Outcome);
        Assert.Contains(excludedResult.Reasons, reason => reason.Contains("included file pattern"));
    }

    [Fact]
    public void ExcludesFilesInsideMatchingDirectory()
    {
        var ignored = Directory.CreateDirectory(Path.Combine(_source, "ignored")).FullName;
        var file = Path.Combine(ignored, "report.txt");
        File.WriteAllText(file, "report");
        var profile = Profile();
        profile.Options.ExcludeDirectories.Add("ignored");

        var result = ProfileRuleTester.Test(profile, file);

        Assert.Equal(RuleTestOutcome.Excluded, result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("directory rule"));
    }

    [Fact]
    public void ExplainsSizeAndAgeExclusions()
    {
        var file = Path.Combine(_source, "old.bin");
        File.WriteAllBytes(file, new byte[20]);
        File.SetLastWriteTime(file, new DateTime(2026, 1, 1));
        var profile = Profile();
        profile.Options.MaxAgeDays = 2;

        var ageResult = ProfileRuleTester.Test(profile, file, new DateTime(2026, 1, 10));
        Assert.Equal(RuleTestOutcome.Excluded, ageResult.Outcome);
        Assert.Contains(ageResult.Reasons, reason => reason.Contains("maximum age"));

        profile.Options.MaxAgeDays = null;
        profile.Options.MaxFileSizeBytes = 10;
        var sizeResult = ProfileRuleTester.Test(profile, file);
        Assert.Equal(RuleTestOutcome.Excluded, sizeResult.Outcome);
        Assert.Contains(sizeResult.Reasons, reason => reason.Contains("maximum"));
    }

    [Fact]
    public void RejectsPathOutsideSource()
    {
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "outside");

        var result = ProfileRuleTester.Test(Profile(), outside);

        Assert.Equal(RuleTestOutcome.CannotEvaluate, result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }
}
