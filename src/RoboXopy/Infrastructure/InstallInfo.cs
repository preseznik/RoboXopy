using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RoboXopy.Infrastructure;

internal enum InstallScope
{
    /// <summary>Not installed by Setup (a copied or developer build); updates are downloaded by hand.</summary>
    Portable,
    CurrentUser,
    AllUsers,
}

/// <summary>How this copy of RoboXopy was installed, which decides how an update is applied.</summary>
internal static class InstallInfo
{
    /// <summary>The uninstall key Inno Setup writes for installer\RoboXopy.iss's AppId.</summary>
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{30B8CBFB-4468-4EB0-A6EA-1DBFC4F98B7A}_is1";

    private static readonly Lazy<InstallScope> LazyScope = new(DetectScope);

    public static InstallScope Scope => LazyScope.Value;

    /// <summary>The runtime identifier in installer file names, e.g. "win-x64".</summary>
    public static string Runtime => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64",
    };

    /// <summary>Where update installers are downloaded to.</summary>
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "RoboXopy-update");

    private static InstallScope DetectScope()
    {
        var appDir = Normalize(AppContext.BaseDirectory);
        if (InstalledIn(RegistryHive.CurrentUser, appDir)) return InstallScope.CurrentUser;
        if (InstalledIn(RegistryHive.LocalMachine, appDir)) return InstallScope.AllUsers;
        return InstallScope.Portable;
    }

    private static bool InstalledIn(RegistryHive hive, string appDir)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && location.Length > 0 &&
                    string.Equals(Normalize(location), appDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
            {
            }
        }

        return false;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
