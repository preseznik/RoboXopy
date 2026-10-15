using System.ComponentModel;
using RoboXopy.Core.Execution;

namespace RoboXopy.Core.Tests;

public sealed class PermissionIssueDetectorTests
{
    [Theory]
    [InlineData("2026/10/15 10:00:00 ERROR 5 (0x00000005) Accessing Source Directory C:\\locked\\")]
    [InlineData("ERROR 5 Scanning Destination Directory")]
    [InlineData("Access is denied.")]
    [InlineData("Permission denied")]
    public void DetectsAccessDeniedOutput(string line)
    {
        Assert.True(PermissionIssueDetector.HasAccessDeniedOutput([line]));
    }

    [Fact]
    public void IgnoresNonPermissionOutput()
    {
        Assert.False(PermissionIssueDetector.HasAccessDeniedOutput(["ERROR 3 (0x00000003) The system cannot find the path specified."]));
    }

    [Fact]
    public void DetectsPermissionExceptions()
    {
        Assert.True(PermissionIssueDetector.IsAccessDenied(new UnauthorizedAccessException()));
        Assert.True(PermissionIssueDetector.IsAccessDenied(new Win32Exception(5)));
        Assert.False(PermissionIssueDetector.IsAccessDenied(new Win32Exception(2)));
    }
}
