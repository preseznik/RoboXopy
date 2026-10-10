using System.Runtime.InteropServices;

namespace RoboXopy.Core.Platform;

/// <summary>
/// Windows RedirectionGuard (the "redirection trust" process mitigation) stops a process from following
/// junctions and symbolic links that non-admin users created. Child processes inherit it and it can't be
/// switched off again, so a robocopy started from such a process silently skips those folders.
/// Installers built with Inno Setup 7 turn it on and pass it to the programs they start.
/// </summary>
public static partial class RedirectionGuard
{
    private const int ProcessRedirectionTrustPolicy = 16;
    private const uint EnforceRedirectionTrust = 1;

    private static readonly Lazy<bool> Enforced = new(Query);

    /// <summary>True when this process – and so every robocopy it starts – has RedirectionGuard enforced.</summary>
    public static bool IsEnforced => Enforced.Value;

    private static bool Query()
    {
        try
        {
            // Fails on Windows versions without this policy, which can't enforce it either.
            return GetProcessMitigationPolicy(GetCurrentProcess(), ProcessRedirectionTrustPolicy, out var flags, sizeof(uint))
                   && (flags & EnforceRedirectionTrust) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMitigationPolicy(nint process, int policy, out uint buffer, nint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
