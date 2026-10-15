using System.ComponentModel;

namespace RoboXopy.Core.Execution;

/// <summary>Recognizes execution failures that may be resolved by running with an elevated token.</summary>
public static class PermissionIssueDetector
{
    public static bool HasAccessDeniedOutput(IEnumerable<string> lines) =>
        lines.Any(line => line.Contains("0x00000005", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ERROR 5", StringComparison.OrdinalIgnoreCase)
            || line.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("permission denied", StringComparison.OrdinalIgnoreCase));

    public static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException
        || exception is Win32Exception { NativeErrorCode: 5 }
        || exception.Message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
}
