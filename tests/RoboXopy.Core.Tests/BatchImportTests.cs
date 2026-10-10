using System.Text;
using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public class BatchImportTests
{
    private const string BatchPath = @"C:\Scripts\robocoy_coding.bat";

    private static BatchCommand Single(string text, string? path = BatchPath)
    {
        var result = BatchFileReader.Parse(text, path);
        return Assert.Single(result.Commands);
    }

    private static RobocopyImportResult Import(string text, string? path = BatchPath)
    {
        var command = Single(text, path);
        return RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory, path);
    }

    [Fact]
    public void SampleBatchFile_BecomesMirrorProfile()
    {
        const string line = "robocopy \"F:\\Coding\" \"U:\\.\" /MIR /COPY:DAT /DCOPY:DAT /R:1 /W:1";
        var command = Single(line);

        Assert.Equal(1, command.LineNumber);
        Assert.Equal(line, command.Text);
        Assert.Empty(command.Notes);

        var result = RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory, BatchPath);
        var p = result.Profile;
        var o = p.Options;

        Assert.False(result.HasErrors);
        Assert.Equal("Coding", p.Name);
        Assert.Equal(@"F:\Coding", p.Source);
        Assert.Equal(@"U:\", p.Destination);
        Assert.Equal(CopyMode.Mirror, o.Mode);
        Assert.Equal(SubdirectoryMode.All, o.Subdirectories);
        Assert.Equal("DAT", RobocopyCommandBuilder.FileCopyFlags(o));
        Assert.Equal("DAT", RobocopyCommandBuilder.DirCopyFlags(o));
        Assert.Equal(1, o.Retries);
        Assert.Equal(1, o.WaitSeconds);
        Assert.False(o.Multithreaded);
        Assert.False(o.ExcludeJunctions);
        Assert.Equal("", o.ExtraArguments);
        Assert.All(result.Items, i => Assert.Equal(ImportItemKind.Mapped, i.Kind));
        Assert.Empty(result.Notes);

        Assert.Equal("robocopy F:\\Coding U:\\ /MIR /DCOPY:DAT /R:1 /W:1", RobocopyCommandBuilder.BuildCommandLine(p));
    }

    [Fact]
    public void Read_DecodesSampleFileWithoutBomOrNewline()
    {
        var dir = Directory.CreateTempSubdirectory("rx-import-");
        try
        {
            var path = Path.Combine(dir.FullName, "robocoy_coding.bat");
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("robocopy \"F:\\Coding\" \"U:\\.\" /MIR /COPY:DAT /DCOPY:DAT /R:1 /W:1"));

            var result = BatchFileReader.Read(path);

            Assert.Equal(path, result.Path);
            var command = Assert.Single(result.Commands);
            Assert.Equal(dir.FullName, command.WorkingDirectory);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Read_RejectsHugeFiles()
    {
        var dir = Directory.CreateTempSubdirectory("rx-import-");
        try
        {
            var path = Path.Combine(dir.FullName, "big.bat");
            File.WriteAllBytes(path, new byte[BatchFileReader.MaxFileBytes + 1]);
            Assert.Throws<InvalidDataException>(() => BatchFileReader.Read(path));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Decode_HonoursBomsUtf8AndOemCodePage()
    {
        Assert.Equal("robocopy \"C:\\Fotografije\\Čebela\"", BatchFileReader.Decode(
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("robocopy \"C:\\Fotografije\\Čebela\"")]));
        Assert.Equal("robocopy Ä", BatchFileReader.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("robocopy Ä")]));
        Assert.Equal("robocopy Ä", BatchFileReader.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("robocopy Ä")]));
        Assert.Equal("robocopy Žaba", BatchFileReader.Decode(Encoding.UTF8.GetBytes("robocopy Žaba")));

        var oem = RobocopyRunner.OemEncoding;
        var bytes = oem.GetBytes("robocopy C:\\Daten\\Übersicht");
        Assert.Equal(oem.GetString(bytes), BatchFileReader.Decode(bytes));
    }

    [Fact]
    public void IgnoresEchoCommentsLabelsAndOtherCommands()
    {
        const string text = """
            @echo off
            rem Nightly backup
            :: robocopy C:\Old D:\Old
            :start
            title Backup
            echo robocopy C:\NotThis D:\NotThis
            robocopy C:\Data D:\Backup /E
            pause
            exit /b %errorlevel%
            """;

        var command = Single(text);
        Assert.Equal(7, command.LineNumber);
        Assert.Equal(@"robocopy C:\Data D:\Backup /E", command.Text);
    }

    [Fact]
    public void NoRobocopyCommand_ReturnsNothing()
    {
        var result = BatchFileReader.Parse("@echo off\r\nxcopy C:\\a D:\\b /E\r\necho robocopy\r\n", BatchPath);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void CaretContinuationsAreJoined()
    {
        const string text = "robocopy \"C:\\My Data\" ^\r\n  \"D:\\Backup\" ^\r\n  /MIR /R:2 /W:3\r\n";
        var command = Single(text);

        Assert.Equal(1, command.LineNumber);
        var result = RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory);
        Assert.Equal(@"C:\My Data", result.Profile.Source);
        Assert.Equal(@"D:\Backup", result.Profile.Destination);
        Assert.Equal(2, result.Profile.Options.Retries);
    }

    [Fact]
    public void ExpandsSetVariablesAndBatchFolder()
    {
        const string text = """
            @echo off
            set "SRC=C:\Users\Me\Documents"
            set DST=%~dp0Backup
            set OPTS=/E /R:0 /W:0
            robocopy "%SRC%" "%DST%" %OPTS%
            """;

        var command = Single(text);
        Assert.Equal(@"robocopy ""C:\Users\Me\Documents"" ""C:\Scripts\Backup"" /E /R:0 /W:0", command.Text);
        Assert.Contains("%SRC%", command.RawText);
        Assert.Empty(command.Notes);
    }

    [Fact]
    public void VariableSubstringAndReplaceOperations()
    {
        const string text = """
            set ROOT=C:\Projects\Alpha
            robocopy %ROOT:Alpha=Beta% D:\%ROOT:~3,8% /E
            """;

        Assert.Equal(@"robocopy C:\Projects\Beta D:\Projects /E", Single(text).Text);
    }

    [Fact]
    public void DelayedExpansion()
    {
        const string text = """
            setlocal EnableDelayedExpansion
            set TARGET=E:\Mirror
            robocopy C:\Data !TARGET! /MIR
            """;

        Assert.Equal(@"robocopy C:\Data E:\Mirror /MIR", Single(text).Text);
    }

    [Fact]
    public void EnvironmentVariablesStayLiteral()
    {
        var result = Import(@"robocopy ""%USERPROFILE%\Documents"" D:\Docs /E");

        Assert.Equal(@"%USERPROFILE%\Documents", result.Profile.Source);
        Assert.DoesNotContain(result.Notes, n => n.Severity != IssueSeverity.Info);
    }

    [Fact]
    public void UndefinedAndRuntimeVariablesWarn()
    {
        const string text = """
            set /p DEST=Where to?
            robocopy C:\Data "%DEST%\%DATE%" /E
            """;

        var command = Single(text);
        Assert.Contains(@"""%DEST%\%DATE%""", command.Text);
        Assert.Equal(2, command.Notes.Count(n => n.Severity == IssueSeverity.Warning));
    }

    [Fact]
    public void BatchArgumentsWarn()
    {
        var command = Single(@"robocopy %1 D:\Backup /E");
        Assert.Equal(@"robocopy %1 D:\Backup /E", command.Text);
        Assert.Contains(command.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("%1"));
    }

    [Fact]
    public void CdAndPushdResolveRelativePaths()
    {
        const string text = """
            cd /d D:\Work
            pushd Projects
            robocopy Site \\server\share\site /MIR
            popd
            """;

        var command = Single(text);
        Assert.Equal(@"D:\Work\Projects", command.WorkingDirectory);

        var result = RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory);
        Assert.Equal(@"D:\Work\Projects\Site", result.Profile.Source);
        Assert.Equal(@"\\server\share\site", result.Profile.Destination);
        Assert.Contains(result.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("relative"));
    }

    [Fact]
    public void RelativePathsDefaultToBatchFolder()
    {
        var result = Import(@"robocopy Docs D:\Docs /E");
        Assert.Equal(@"C:\Scripts\Docs", result.Profile.Source);
    }

    [Fact]
    public void RedirectionAndChainedCommandsAreIgnored()
    {
        var command = Single(@"robocopy C:\A D:\B /E > ""C:\logs\out.txt"" 2>&1 && echo done || echo failed");

        Assert.Equal(@"robocopy C:\A D:\B /E", command.Text);
        Assert.Contains(command.Notes, n => n.Message.Contains("redirection"));
        Assert.Contains(command.Notes, n => n.Message.Contains("echo done"));
    }

    [Fact]
    public void PipeIsIgnored()
    {
        var command = Single(@"robocopy C:\A D:\B /E | find ""Copied""");
        Assert.Equal(@"robocopy C:\A D:\B /E", command.Text);
    }

    [Fact]
    public void AmpersandInsideQuotesIsKept()
    {
        var command = Single(@"robocopy ""C:\Tom & Jerry"" D:\B /E");
        Assert.Equal(@"robocopy ""C:\Tom & Jerry"" D:\B /E", command.Text);
    }

    [Fact]
    public void ConditionsWarn()
    {
        var command = Single(@"if exist D:\ robocopy C:\A D:\A /E");
        Assert.Equal(@"robocopy C:\A D:\A /E", command.Text);
        Assert.Contains(command.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("if exist"));
    }

    [Fact]
    public void BlocksWarn()
    {
        const string text = """
            if exist E:\ (
                robocopy C:\A E:\A /MIR
            )
            """;

        var command = Single(text);
        Assert.Equal(@"robocopy C:\A E:\A /MIR", command.Text);
        Assert.Contains(command.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains(@"if exist E:\"));
    }

    [Fact]
    public void ParenthesisedCommandOnOneLine()
    {
        var command = Single(@"if exist E:\ (robocopy ""C:\A (old)"" E:\A /MIR) else (echo missing)");
        Assert.Equal(@"robocopy ""C:\A (old)"" E:\A /MIR", command.Text);
    }

    [Fact]
    public void CallAndStartPrefixes()
    {
        Assert.Empty(Single(@"call robocopy C:\A D:\A /E").Notes);

        var start = Single(@"start """" /wait /min robocopy C:\A D:\A /E");
        Assert.Equal(@"robocopy C:\A D:\A /E", start.Text);
        Assert.Contains(start.Notes, n => n.Message.Contains("start"));

        var cmd = Single(@"cmd /c robocopy C:\A D:\A /E");
        Assert.Equal(@"robocopy C:\A D:\A /E", cmd.Text);
        Assert.Contains(cmd.Notes, n => n.Message.Contains("cmd /c"));
    }

    [Fact]
    public void FullRobocopyPathIsAccepted()
    {
        var command = Single(@"""C:\Windows\System32\Robocopy.exe"" C:\A D:\A /E");
        var result = RobocopyCommandParser.Parse(command.Text, command.WorkingDirectory);
        Assert.Equal(@"C:\A", result.Profile.Source);
        Assert.Equal(@"D:\A", result.Profile.Destination);
    }

    [Fact]
    public void FindsEveryRobocopyCommand()
    {
        const string text = """
            robocopy C:\Photos E:\Photos /MIR
            robocopy C:\Music E:\Music /MIR
            """;

        var result = BatchFileReader.Parse(text, BatchPath);
        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(2, result.Commands[1].LineNumber);
    }

    [Fact]
    public void ExclusionListsAndIncludePatterns()
    {
        var result = Import(@"robocopy C:\Src D:\Dst *.docx *.xlsx /S /XF *.tmp ""~$*"" /XD node_modules ""C:\Src\Cache"" /XO");
        var o = result.Profile.Options;

        Assert.Equal(["*.docx", "*.xlsx"], o.IncludeFiles);
        Assert.Equal(["*.tmp", "~$*"], o.ExcludeFiles);
        Assert.Equal(["node_modules", @"C:\Src\Cache"], o.ExcludeDirectories);
        Assert.True(o.ExcludeOlder);
        Assert.Equal(SubdirectoryMode.NonEmpty, o.Subdirectories);
        Assert.Contains(result.Items, i => i.Argument == @"/XD node_modules C:\Src\Cache" && i.Description == @"Exclude folders: node_modules, C:\Src\Cache");
        Assert.Contains(result.Items, i => i.Description == "Only include files: *.docx, *.xlsx");
    }

    [Fact]
    public void ModeAndFlagMapping()
    {
        var o = Import(@"robocopy C:\A D:\A /MOVE /COPYALL /DCOPY:T /MT:16 /ZB /J /R:5 /W:10 /MAXAGE:30 /MAX:1048576 /M /NP /NFL").Profile.Options;

        Assert.Equal(CopyMode.MoveAll, o.Mode);
        Assert.Equal(SubdirectoryMode.None, o.Subdirectories);
        Assert.Equal("DATSOU", RobocopyCommandBuilder.FileCopyFlags(o));
        Assert.Equal("T", RobocopyCommandBuilder.DirCopyFlags(o));
        Assert.True(o.Multithreaded);
        Assert.Equal(16, o.ThreadCount);
        Assert.Equal(RestartMode.RestartableWithBackupFallback, o.RestartMode);
        Assert.True(o.Unbuffered);
        Assert.Equal(30, o.MaxAgeDays);
        Assert.Equal(1048576, o.MaxFileSizeBytes);
        Assert.Equal(ArchiveMode.OnlyArchivedAndReset, o.Archive);
        Assert.True(o.NoProgress);
        Assert.True(o.NoFileList);
    }

    [Fact]
    public void CopyWithPurgeAndSeparateRestartSwitches()
    {
        var o = Import(@"robocopy C:\A D:\A /E /PURGE /Z /B /MT").Profile.Options;

        Assert.Equal(CopyMode.Copy, o.Mode);
        Assert.True(o.Purge);
        Assert.Equal(SubdirectoryMode.All, o.Subdirectories);
        Assert.Equal(RestartMode.RestartableWithBackupFallback, o.RestartMode);
        Assert.Equal(8, o.ThreadCount);
    }

    [Fact]
    public void MissingSwitchesUseRobocopySemantics()
    {
        var result = Import(@"robocopy C:\A D:\A");
        var o = result.Profile.Options;

        Assert.Equal(SubdirectoryMode.None, o.Subdirectories);
        Assert.False(o.Multithreaded);
        Assert.Equal("DA", RobocopyCommandBuilder.DirCopyFlags(o));
        Assert.Equal(3, o.Retries);
        Assert.Equal(5, o.WaitSeconds);
        Assert.Contains(result.Notes, n => n.Message.Contains("1,000,000"));
        Assert.Contains(result.Notes, n => n.Message.Contains("subfolders"));
    }

    [Fact]
    public void UnsupportedSwitchesGoToExtraArguments()
    {
        var result = Import(@"robocopy C:\A D:\A /E /R:1 /W:1 /XA:H /LEV:2 /FOO");
        var o = result.Profile.Options;

        Assert.Equal("/XA:H /LEV:2 /FOO", o.ExtraArguments);
        Assert.Equal(3, result.Items.Count(i => i.Kind == ImportItemKind.Extra));
        Assert.Contains(result.Notes, n => n.Severity == IssueSeverity.Info && n.Message.Contains("/XA:H and /LEV:2 have"));
        Assert.Contains(result.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("/FOO"));
        Assert.EndsWith("/XA:H /LEV:2 /FOO", RobocopyCommandBuilder.BuildCommandLine(result.Profile));
    }

    [Fact]
    public void LoggingListOnlyAndMonitorSwitchesAreDropped()
    {
        var result = Import(@"robocopy C:\A D:\A /MIR /R:1 /W:1 /L /LOG+:""C:\logs\sync.log"" /TEE /NJH /NJS /MON:1");
        var o = result.Profile.Options;

        Assert.Equal("", o.ExtraArguments);
        Assert.Equal(6, result.Items.Count(i => i.Kind == ImportItemKind.Dropped));
        Assert.Contains(result.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("Preview"));
        Assert.Contains(result.Notes, n => n.Severity == IssueSeverity.Warning && n.Message.Contains("schedule"));

        var command = RobocopyCommandBuilder.BuildCommandLine(result.Profile);
        Assert.DoesNotContain("/L", command.Split(' '));
        Assert.DoesNotContain("/LOG", command);
    }

    [Fact]
    public void InvalidValuesAreDropped()
    {
        var result = Import(@"robocopy C:\A D:\A /E /R:x /W:1 /MT:500");

        Assert.Equal(3, result.Profile.Options.Retries);
        Assert.Equal(128, result.Profile.Options.ThreadCount);
        Assert.Contains(result.Items, i => i.Argument == "/R:x" && i.Kind == ImportItemKind.Dropped);
        Assert.Contains(result.Notes, n => n.Message.Contains("128"));
    }

    [Fact]
    public void MissingDestinationIsAnError()
    {
        var result = RobocopyCommandParser.Parse(@"robocopy C:\A /E");
        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData(@"C:\x\robocoy_coding.bat", @"C:\Data", "Coding")]
    [InlineData(@"C:\x\Robocopy-Photos to NAS.cmd", @"C:\Data", "Photos to NAS")]
    [InlineData(@"C:\x\robocopy.bat", @"C:\Users\Me\Music", "Music")]
    [InlineData(@"C:\x\robocopy.bat", @"%USERPROFILE%", RobocopyCommandParser.DefaultName)]
    [InlineData(null, @"C:\", RobocopyCommandParser.DefaultName)]
    [InlineData(@"C:\x\nightly_backup.bat", "", "Nightly backup")]
    public void SuggestsNames(string? batchPath, string source, string expected)
    {
        Assert.Equal(expected, RobocopyCommandParser.SuggestName(batchPath, source));
    }

    [Theory]
    [InlineData(@"robocopy ""C:\Src"" ""D:\Dst"" /MIR /R:2 /W:2")]
    [InlineData(@"robocopy C:\Src D:\Dst *.jpg /E /PURGE /COPY:DATSO /DCOPY:T /MT:4 /Z /XF *.tmp /XD ""C:\Src\Skip Me"" /XO /XN /MINAGE:2 /MIN:10 /A /R:0 /W:0 /V /TS /FP /BYTES")]
    [InlineData(@"robocopy C:\Src D:\Dst /S /MOV /IPG:50 /J /COMPRESS /XJ /SL /FFT /DST /IS /IT /XC /XX /XL /R:1 /W:1 /NDL")]
    public void RoundTripsThroughCommandBuilder(string commandLine)
    {
        var first = RobocopyCommandParser.Parse(commandLine, @"C:\");
        var rebuilt = RobocopyCommandBuilder.BuildCommandLine(first.Profile);
        var second = RobocopyCommandParser.Parse(rebuilt, @"C:\");

        Assert.Equal(rebuilt, RobocopyCommandBuilder.BuildCommandLine(second.Profile));
        Assert.False(first.HasErrors);
    }
}
