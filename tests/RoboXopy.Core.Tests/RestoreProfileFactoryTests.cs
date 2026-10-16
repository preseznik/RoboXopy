using RoboXopy.Core.Models;

namespace RoboXopy.Core.Tests;

public sealed class RestoreProfileFactoryTests
{
    [Fact]
    public void CreatesSafeUnscheduledReverseCopy()
    {
        var original = new SyncProfile
        {
            Name = "Documents",
            Source = @"C:\Documents",
            Destination = @"E:\Backups\Documents",
            SortOrder = 3,
        };
        original.Options.Mode = CopyMode.Mirror;
        original.Options.Purge = true;
        original.Options.ExtraArguments = "/MOVE";
        original.Options.ExcludeFiles.Add("*.tmp");
        original.Schedule.Enabled = true;

        var restore = RestoreProfileFactory.Create(original, original.Destination, original.Source);

        Assert.Equal(original.Id, restore.Id);
        Assert.Equal(original.Destination, restore.Source);
        Assert.Equal(original.Source, restore.Destination);
        Assert.Equal(CopyMode.Copy, restore.Options.Mode);
        Assert.False(restore.Options.Purge);
        Assert.Empty(restore.Options.ExtraArguments);
        Assert.Empty(restore.Options.ExcludeFiles);
        Assert.False(restore.Schedule.Enabled);
    }
}
