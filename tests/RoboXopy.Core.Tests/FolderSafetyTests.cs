using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class FolderSafetyTests
{
    [Theory]
    [InlineData(@"D:\archives", @"D:\archives")]
    [InlineData(@"D:\archives\backup", @"D:\archives")]
    [InlineData(@"D:\", @"D:\archives")]
    public void DetectsDestinationOverlappingHistoricalSource(string destination, string source)
    {
        Assert.True(FolderSafety.DestinationOverlapsSourceHistory(destination, [source]));
    }

    [Fact]
    public void AllowsSeparateDestination()
    {
        Assert.False(FolderSafety.DestinationOverlapsSourceHistory(@"D:\backup", [@"D:\archives"]));
    }

    [Fact]
    public void ConfirmationOnlyAppliesToItsExactDestination()
    {
        var profile = new SyncProfile
        {
            Destination = @"D:\archives",
            ConfirmedHistoricalSourceDestination = @"D:\archives",
        };

        Assert.True(FolderSafety.HasConfirmedHistoricalSourceDestination(profile));
        profile.Destination = @"D:\archives\backup";
        Assert.False(FolderSafety.HasConfirmedHistoricalSourceDestination(profile));
    }
}
