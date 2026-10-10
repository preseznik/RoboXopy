using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public class CommandBuilderTests
{
    private static SyncProfile Profile(string src = @"C:\Data", string dst = @"D:\Backup") => new() { Source = src, Destination = dst };

    [Fact]
    public void Defaults_ProduceCopyWithSubdirectories()
    {
        var args = RobocopyCommandBuilder.BuildArguments(Profile());

        Assert.Equal(@"C:\Data", args[0]);
        Assert.Equal(@"D:\Backup", args[1]);
        Assert.Contains("/E", args);
        Assert.Contains("/MT:8", args);
        Assert.Contains("/R:3", args);
        Assert.Contains("/W:5", args);
        Assert.Contains("/XJ", args);
        Assert.DoesNotContain("/MIR", args);
        Assert.DoesNotContain("/L", args);
        Assert.DoesNotContain(args, a => a.StartsWith("/COPY:"));
    }

    [Fact]
    public void Mirror_UsesMirAndNoSubdirectorySwitch()
    {
        var p = Profile();
        p.Options.Mode = CopyMode.Mirror;
        var args = RobocopyCommandBuilder.BuildArguments(p);

        Assert.Contains("/MIR", args);
        Assert.DoesNotContain("/E", args);
        Assert.DoesNotContain("/PURGE", args);
    }

    [Fact]
    public void CopyWithPurge_AddsPurge()
    {
        var p = Profile();
        p.Options.Purge = true;
        Assert.Contains("/PURGE", RobocopyCommandBuilder.BuildArguments(p));
    }

    [Theory]
    [InlineData(CopyMode.MoveFiles, "/MOV")]
    [InlineData(CopyMode.MoveAll, "/MOVE")]
    public void MoveModes(CopyMode mode, string expected)
    {
        var p = Profile();
        p.Options.Mode = mode;
        var args = RobocopyCommandBuilder.BuildArguments(p);
        Assert.Contains(expected, args);
        Assert.Contains("/E", args);
    }

    [Fact]
    public void DryRun_AppendsListOnly()
    {
        var args = RobocopyCommandBuilder.BuildArguments(Profile(), dryRun: true);
        Assert.Equal("/L", args[^1]);
    }

    [Fact]
    public void Filters_AreEmittedAndCleaned()
    {
        var p = Profile();
        p.Options.IncludeFiles.Add("*.docx");
        p.Options.IncludeFiles.Add("  ");
        p.Options.ExcludeFiles.Add("*.tmp");
        p.Options.ExcludeFiles.Add("~$*");
        p.Options.ExcludeFiles.Add("*.TMP");
        p.Options.ExcludeDirectories.Add(@"node_modules");
        p.Options.ExcludeDirectories.Add(@"C:\Data\Cache\");
        var args = RobocopyCommandBuilder.BuildArguments(p).ToList();

        Assert.Equal("*.docx", args[2]);
        var xf = args.IndexOf("/XF");
        Assert.Equal(["*.tmp", "~$*"], args.Skip(xf + 1).Take(2));
        Assert.Equal("/XD", args[xf + 3]);
        var xd = args.IndexOf("/XD");
        Assert.Equal(["node_modules", @"C:\Data\Cache"], args.Skip(xd + 1).Take(2));
    }

    [Fact]
    public void CopyFlags_OnlyEmittedWhenNotDefault()
    {
        var p = Profile();
        p.Options.CopyFileSecurity = true;
        p.Options.CopyFileOwner = true;
        p.Options.CopyDirTimestamps = true;
        var args = RobocopyCommandBuilder.BuildArguments(p);

        Assert.Contains("/COPY:DATSO", args);
        Assert.Contains("/DCOPY:DAT", args);
    }

    [Fact]
    public void Throttling_DisablesMultithreading()
    {
        var p = Profile();
        p.Options.InterPacketGapMs = 50;
        var args = RobocopyCommandBuilder.BuildArguments(p);

        Assert.Contains("/IPG:50", args);
        Assert.DoesNotContain(args, a => a.StartsWith("/MT"));
    }

    [Fact]
    public void AgeSizeAndArchiveFilters()
    {
        var p = Profile();
        p.Options.MaxAgeDays = 30;
        p.Options.MinFileSizeBytes = 1024;
        p.Options.Archive = ArchiveMode.OnlyArchivedAndReset;
        p.Options.RestartMode = RestartMode.RestartableWithBackupFallback;
        var args = RobocopyCommandBuilder.BuildArguments(p);

        Assert.Contains("/MAXAGE:30", args);
        Assert.Contains("/MIN:1024", args);
        Assert.Contains("/M", args);
        Assert.Contains("/ZB", args);
    }

    [Fact]
    public void ExtraArguments_AreTokenized()
    {
        var p = Profile();
        p.Options.ExtraArguments = "/XA:H \"/UNILOG+:C:\\My Logs\\x.log\"";
        var args = RobocopyCommandBuilder.BuildArguments(p);

        Assert.Contains("/XA:H", args);
        Assert.Contains(@"/UNILOG+:C:\My Logs\x.log", args);
    }

    [Fact]
    public void CommandLine_QuotesPathsWithSpaces()
    {
        var p = Profile(@"C:\My Data\", @"\\nas\backup share");
        var line = RobocopyCommandBuilder.BuildCommandLine(p);
        Assert.StartsWith("robocopy \"C:\\My Data\" \"\\\\nas\\backup share\" ", line);
    }
}
