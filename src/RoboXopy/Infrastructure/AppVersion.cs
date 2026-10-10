using System.Reflection;

namespace RoboXopy.Infrastructure;

/// <summary>Version information stamped by Directory.Build.props / tools\publish.ps1.</summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    /// <summary>Full informational version, e.g. "1.2.0-beta.1+build.14.20260518".</summary>
    public static string Informational { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Semantic version without build metadata, e.g. "1.2.0-beta.1".</summary>
    public static string SemVer { get; } = Informational.Split('+')[0];

    /// <summary>True for builds that didn't go through the publish script.</summary>
    public static bool IsDevBuild { get; } = Informational.EndsWith("+dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>Monotonic build number (4th part of the file version).</summary>
    public static int BuildNumber { get; } =
        Version.TryParse(Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version, out var file) && file.Revision > 0
            ? file.Revision
            : 0;

    /// <summary>Human-friendly version, e.g. "1.2.0 (build 14)" or "1.2.0 (dev build)".</summary>
    public static string Display => IsDevBuild || BuildNumber == 0
        ? $"{SemVer} (dev build)"
        : $"{SemVer} (build {BuildNumber})";
}
