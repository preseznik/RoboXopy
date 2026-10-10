using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RoboXopy.Core.Robocopy;

public sealed record RobocopyProcessResult(int ExitCode, IReadOnlyList<string> Lines);

/// <summary>Runs robocopy.exe, streaming its output line by line.</summary>
public static partial class RobocopyRunner
{
    private static readonly Lazy<Encoding> ConsoleEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(437);
        }
    });

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    /// <param name="onLine">Raised on a background thread for every output line (including progress lines).</param>
    /// <param name="onStarted">Raised with the process id once robocopy has started.</param>
    public static async Task<RobocopyProcessResult> RunAsync(
        string robocopyPath,
        IReadOnlyList<string> arguments,
        Action<string>? onLine,
        CancellationToken cancellationToken,
        Action<int>? onStarted = null)
    {
        var psi = new ProcessStartInfo(robocopyPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding.Value,
            StandardErrorEncoding = ConsoleEncoding.Value,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        onStarted?.Invoke(process.Id);

        var lines = new List<string>();
        var sync = new object();

        void Handle(string line)
        {
            lock (sync) lines.Add(line);
            onLine?.Invoke(line);
        }

        var stdout = PumpAsync(process.StandardOutput, Handle);
        var stderr = PumpAsync(process.StandardError, Handle);

        await using (cancellationToken.Register(() =>
                     {
                         try
                         {
                             if (!process.HasExited) process.Kill(entireProcessTree: true);
                         }
                         catch (InvalidOperationException)
                         {
                         }
                         catch (System.ComponentModel.Win32Exception)
                         {
                         }
                     }))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }

        var exitCode = cancellationToken.IsCancellationRequested ? RobocopyExitCode.Cancelled : process.ExitCode;
        lock (sync) return new RobocopyProcessResult(exitCode, lines.ToList());
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> handle)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            handle(line);
        }
    }
}
