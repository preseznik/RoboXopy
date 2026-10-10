using System.Runtime.InteropServices;

namespace RoboXopy.Infrastructure;

internal static partial class NativeMethods
{
    public const int AttachParentProcess = -1;
    public const int SwRestore = 9;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT: only restart after an installer closed the app.</summary>
    public const int RestartOnlyAfterUpdate = 1 | 2 | 8;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterApplicationRestart(string commandLineArgs, int flags);

    public const int SmCxSizeFrame = 32;
    public const int SmCxPaddedBorder = 92;

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);
}
