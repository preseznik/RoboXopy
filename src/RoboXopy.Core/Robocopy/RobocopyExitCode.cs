using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

/// <summary>Interprets robocopy's bit-flag exit codes.</summary>
public static class RobocopyExitCode
{
    public const int Cancelled = -2;
    public const int StartFailed = -1;

    public static RunOutcome ToOutcome(int code) => code switch
    {
        Cancelled => RunOutcome.Cancelled,
        < 0 => RunOutcome.Error,
        >= 8 => RunOutcome.Failed,
        _ when (code & 4) != 0 => RunOutcome.Warning,
        _ => RunOutcome.Success,
    };

    public static string Describe(int code, bool preview = false, bool purge = false)
    {
        if (code == Cancelled) return "Cancelled";
        if (code < 0) return "Robocopy could not be started";
        if (code == 0) return "No changes – source and destination are in sync";

        var parts = new List<string>();
        if ((code & 16) != 0) parts.Add("Fatal error – nothing was copied");
        if ((code & 8) != 0) parts.Add(preview ? "Some files or folders could not be read" : "Some files or folders could not be copied");
        if ((code & 1) != 0) parts.Add(preview ? "Files would be copied" : "Files were copied");
        if ((code & 2) != 0)
        {
            parts.Add(!purge ? "Extra files found in the destination"
                : preview ? "Extra files would be removed from the destination"
                : "Extra files were removed from the destination");
        }
        if ((code & 4) != 0) parts.Add("Mismatched files or folders detected");
        return string.Join(" · ", parts);
    }
}
