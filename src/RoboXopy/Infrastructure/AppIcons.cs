using RoboXopy.Core.Models;

namespace RoboXopy.Infrastructure;

internal static class AppIcons
{
    public static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("RoboXopy.ico")
                           ?? throw new InvalidOperationException("Embedded icon is missing.");
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public static System.Windows.Forms.ToolTipIcon ToolTipIconFor(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Success => System.Windows.Forms.ToolTipIcon.Info,
        RunOutcome.Warning or RunOutcome.Cancelled => System.Windows.Forms.ToolTipIcon.Warning,
        RunOutcome.Failed or RunOutcome.Error => System.Windows.Forms.ToolTipIcon.Error,
        _ => System.Windows.Forms.ToolTipIcon.None,
    };
}
