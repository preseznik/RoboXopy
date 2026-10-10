using RoboXopy.Core.Models;
using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public class OutputParserTests
{
    private static readonly string[] EnglishOutput =
    [
        "-------------------------------------------------------------------------------",
        "   ROBOCOPY     ::     Robust File Copy for Windows                              ",
        "-------------------------------------------------------------------------------",
        "",
        "  Started : 10 October 2026 00:17:32",
        @"   Source : C:\src\",
        @"     Dest : C:\dst\",
        "",
        "    Files : *.*",
        "\t    ",
        "  Options : *.* /V /S /E /DCOPY:DA /COPY:DAT /PURGE /MIR /R:1 /W:1 ",
        "",
        "------------------------------------------------------------------------------",
        "",
        "\t                   1\tC:\\src\\",
        "\t*EXTRA Dir        -1\tC:\\dst\\old\\",
        "\t  *EXTRA File \t\t       3\textra.txt",
        "\t    New File  \t\t       7\ta.txt",
        "100%  ",
        "\t  New Dir          1\tC:\\src\\sub\\",
        "\t    New File  \t\t   2.8 m\tb.txt",
        " 34%  ",
        "100%  ",
        "",
        "------------------------------------------------------------------------------",
        "",
        "               Total    Copied   Skipped  Mismatch    FAILED    Extras",
        "    Dirs :         2         1         1         0         0         1",
        "   Files :         2         2         0         0         0         1",
        "   Bytes :    2.86 m    2.86 m         0         0         0         3",
        "   Times :   0:00:00   0:00:00                       0:00:00   0:00:00",
        "",
        "",
        "   Speed :           1,000,003,000 Bytes/sec.",
        "   Ended : 10 October 2026 00:17:32",
    ];

    [Fact]
    public void ParsesSummary()
    {
        var summary = RobocopyOutputParser.ParseSummary(EnglishOutput);

        Assert.NotNull(summary);
        Assert.Equal(new SummaryRow { Total = 2, Copied = 1, Skipped = 1, Mismatch = 0, Failed = 0, Extras = 1 }, summary.Dirs);
        Assert.Equal(2, summary.Files.Total);
        Assert.Equal(1, summary.Files.Extras);
        Assert.Equal((long)Math.Round(2.86 * 1024 * 1024), summary.Bytes.Total);
        Assert.Equal(3, summary.Bytes.Extras);
    }

    [Fact]
    public void ParsesLocalizedSummaryWithCommaDecimals()
    {
        string[] german =
        [
            "------------------------------------------------------------------------------",
            "",
            "                  Insgesamt   KopiertÜbersprungenKeine Übereinstimmung    FEHLER    Extras",
            "Verzeich.:         5         0         5         0         0         0",
            "  Dateien:        12         3         9         0         1         0",
            "    Bytes:    1,25 g   512,0 k    1,24 g         0       100         0",
            "   Zeiten:   0:00:01   0:00:00                       0:00:00   0:00:00",
        ];

        var summary = RobocopyOutputParser.ParseSummary(german);

        Assert.NotNull(summary);
        Assert.Equal(5, summary.Dirs.Total);
        Assert.Equal(3, summary.Files.Copied);
        Assert.Equal(1, summary.Files.Failed);
        Assert.Equal((long)Math.Round(1.25 * (1L << 30)), summary.Bytes.Total);
        Assert.Equal(512 * 1024, summary.Bytes.Copied);
        Assert.Equal(100, summary.Bytes.Failed);
    }

    [Fact]
    public void MissingSummary_ReturnsNull()
    {
        Assert.Null(RobocopyOutputParser.ParseSummary(["hello", "world"]));
    }

    [Fact]
    public void ClassifiesLines()
    {
        var file = RobocopyOutputParser.Parse("\t    New File  \t\t   2.8 m\tb.txt");
        Assert.Equal(OutputLineKind.File, file.Kind);
        Assert.Equal("New File", file.Status);
        Assert.Equal("b.txt", file.Name);
        Assert.Equal((long)Math.Round(2.8 * 1024 * 1024), file.Size);

        var extra = RobocopyOutputParser.Parse("\t  *EXTRA File \t\t       3\textra.txt");
        Assert.Equal(OutputLineKind.File, extra.Kind);
        Assert.True(extra.IsExtra);

        var dir = RobocopyOutputParser.Parse("\t  New Dir          1\tC:\\src\\sub\\");
        Assert.Equal(OutputLineKind.Directory, dir.Kind);
        Assert.Equal(@"C:\src\sub\", dir.Name);

        var progress = RobocopyOutputParser.Parse(" 34.5%  ");
        Assert.Equal(OutputLineKind.Progress, progress.Kind);
        Assert.Equal(34.5, progress.Percent);

        var error = RobocopyOutputParser.Parse("2026/10/10 00:17:32 ERROR 5 (0x00000005) Copying File C:\\src\\locked.txt");
        Assert.Equal(OutputLineKind.Error, error.Kind);

        Assert.Equal(OutputLineKind.Text, RobocopyOutputParser.Parse("   Source : C:\\src\\").Kind);
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1234", 1234L)]
    [InlineData("1.5 k", 1536L)]
    [InlineData("2 g", 2147483648L)]
    [InlineData("1,5 m", 1572864L)]
    public void ParsesSizes(string text, long expected) => Assert.Equal(expected, RobocopyOutputParser.ParseSize(text));
}

public class ExitCodeTests
{
    [Theory]
    [InlineData(0, RunOutcome.Success)]
    [InlineData(1, RunOutcome.Success)]
    [InlineData(3, RunOutcome.Success)]
    [InlineData(4, RunOutcome.Warning)]
    [InlineData(7, RunOutcome.Warning)]
    [InlineData(8, RunOutcome.Failed)]
    [InlineData(16, RunOutcome.Failed)]
    [InlineData(RobocopyExitCode.Cancelled, RunOutcome.Cancelled)]
    [InlineData(RobocopyExitCode.StartFailed, RunOutcome.Error)]
    public void MapsOutcome(int code, RunOutcome expected) => Assert.Equal(expected, RobocopyExitCode.ToOutcome(code));

    [Fact]
    public void DescribesFlags()
    {
        Assert.Contains("in sync", RobocopyExitCode.Describe(0));
        Assert.Equal("Files were copied · Extra files found in the destination", RobocopyExitCode.Describe(3));
        Assert.Contains("would be copied", RobocopyExitCode.Describe(1, preview: true));
        Assert.Equal("Extra files were removed from the destination", RobocopyExitCode.Describe(2, purge: true));
        Assert.Equal("Extra files would be removed from the destination", RobocopyExitCode.Describe(2, preview: true, purge: true));
    }
}
