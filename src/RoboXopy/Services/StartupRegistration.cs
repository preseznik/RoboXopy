using Microsoft.Win32;

namespace RoboXopy.Services;

/// <summary>Adds or removes RoboXopy from the current user's "Run" key so it starts with Windows.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RoboXopy";

    public static string CommandFor(string exePath) => $"\"{exePath}\" --autostart{Core.Storage.AppPaths.Default.LaunchArguments}";

    public static bool IsRegistered(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && string.Equals(value, CommandFor(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes the startup entry if it launches <paramref name="exePath"/>; returns true when one was removed.</summary>
    public static bool RemoveFor(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string value || !value.Contains($"\"{exePath}\"", StringComparison.OrdinalIgnoreCase)) return false;
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    public static void Apply(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var command = CommandFor(exePath);
                if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Not fatal; the setting is retried on the next start.
        }
    }
}
