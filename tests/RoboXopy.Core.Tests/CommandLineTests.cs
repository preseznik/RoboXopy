using RoboXopy.Core.Robocopy;

namespace RoboXopy.Core.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has space", "\"has space\"")]
    [InlineData(@"C:\a b\", "\"C:\\a b\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void Quote(string input, string expected) => Assert.Equal(expected, CommandLine.Quote(input));

    [Fact]
    public void Split_HonoursQuotes()
    {
        Assert.Equal(["/A", "b c", "/D", ""], CommandLine.Split("/A \"b c\"  /D \"\""));
        Assert.Empty(CommandLine.Split("   "));
        Assert.Empty(CommandLine.Split(null));
    }

    [Theory]
    [InlineData(@"C:\Data\", @"C:\Data")]
    [InlineData(@"  ""C:\Data\\""  ", @"C:\Data")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:", @"C:\")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData("", "")]
    public void NormalizePath(string input, string expected) => Assert.Equal(expected, CommandLine.NormalizePath(input));
}
