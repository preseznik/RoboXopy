using System.Globalization;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

/// <summary>Translates a <see cref="SyncProfile"/> into robocopy arguments.</summary>
public static class RobocopyCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(SyncProfile profile, bool dryRun = false)
    {
        var o = profile.Options;
        var args = new List<string>
        {
            CommandLine.NormalizePath(profile.Source),
            CommandLine.NormalizePath(profile.Destination),
        };

        args.AddRange(CleanPatterns(o.IncludeFiles));

        switch (o.Mode)
        {
            case CopyMode.Mirror:
                args.Add("/MIR");
                break;
            case CopyMode.MoveFiles:
                AddSubdirectories(args, o.Subdirectories);
                args.Add("/MOV");
                break;
            case CopyMode.MoveAll:
                AddSubdirectories(args, o.Subdirectories);
                args.Add("/MOVE");
                break;
            default:
                AddSubdirectories(args, o.Subdirectories);
                if (o.Purge) args.Add("/PURGE");
                break;
        }

        var fileFlags = FileCopyFlags(o);
        if (fileFlags.Length > 0 && fileFlags != "DAT") args.Add($"/COPY:{fileFlags}");

        var dirFlags = DirCopyFlags(o);
        if (dirFlags.Length > 0 && dirFlags != "DA") args.Add($"/DCOPY:{dirFlags}");

        var throttled = o.InterPacketGapMs is > 0;
        if (o.Multithreaded && !throttled) args.Add($"/MT:{Math.Clamp(o.ThreadCount, 1, 128)}");

        switch (o.RestartMode)
        {
            case RestartMode.Restartable: args.Add("/Z"); break;
            case RestartMode.Backup: args.Add("/B"); break;
            case RestartMode.RestartableWithBackupFallback: args.Add("/ZB"); break;
        }

        if (o.Unbuffered) args.Add("/J");
        if (o.Compress) args.Add("/COMPRESS");
        if (o.ExcludeJunctions) args.Add("/XJ");
        if (o.CopySymlinksAsLinks) args.Add("/SL");
        if (o.FatFileTimes) args.Add("/FFT");
        if (o.CompensateDst) args.Add("/DST");
        if (throttled) args.Add($"/IPG:{o.InterPacketGapMs}");

        var excludeFiles = CleanPatterns(o.ExcludeFiles);
        if (excludeFiles.Count > 0)
        {
            args.Add("/XF");
            args.AddRange(excludeFiles);
        }

        var excludeDirs = CleanPatterns(o.ExcludeDirectories);
        if (excludeDirs.Count > 0)
        {
            args.Add("/XD");
            args.AddRange(excludeDirs.Select(CommandLine.NormalizePath));
        }

        if (o.ExcludeOlder) args.Add("/XO");
        if (o.ExcludeNewer) args.Add("/XN");
        if (o.ExcludeChanged) args.Add("/XC");
        if (o.ExcludeExtra) args.Add("/XX");
        if (o.ExcludeLonely) args.Add("/XL");
        if (o.IncludeSame) args.Add("/IS");
        if (o.IncludeTweaked) args.Add("/IT");

        if (o.MaxAgeDays is > 0) args.Add($"/MAXAGE:{o.MaxAgeDays}");
        if (o.MinAgeDays is > 0) args.Add($"/MINAGE:{o.MinAgeDays}");
        if (o.MaxFileSizeBytes is > 0) args.Add($"/MAX:{o.MaxFileSizeBytes.Value.ToString(CultureInfo.InvariantCulture)}");
        if (o.MinFileSizeBytes is > 0) args.Add($"/MIN:{o.MinFileSizeBytes.Value.ToString(CultureInfo.InvariantCulture)}");

        switch (o.Archive)
        {
            case ArchiveMode.OnlyArchived: args.Add("/A"); break;
            case ArchiveMode.OnlyArchivedAndReset: args.Add("/M"); break;
        }

        args.Add($"/R:{Math.Max(0, o.Retries)}");
        args.Add($"/W:{Math.Max(0, o.WaitSeconds)}");

        if (o.Verbose) args.Add("/V");
        if (o.Timestamps) args.Add("/TS");
        if (o.FullPaths) args.Add("/FP");
        if (o.NoFileList) args.Add("/NFL");
        if (o.NoDirList) args.Add("/NDL");
        if (o.Bytes) args.Add("/BYTES");
        if (o.NoProgress) args.Add("/NP");

        args.AddRange(CommandLine.Split(o.ExtraArguments));

        if (dryRun) args.Add("/L");

        return args;
    }

    public static string BuildCommandLine(SyncProfile profile, bool dryRun = false) =>
        "robocopy " + CommandLine.Join(BuildArguments(profile, dryRun));

    public static string FileCopyFlags(RobocopyOptions o)
    {
        var flags = "";
        if (o.CopyFileData) flags += "D";
        if (o.CopyFileAttributes) flags += "A";
        if (o.CopyFileTimestamps) flags += "T";
        if (o.CopyFileSecurity) flags += "S";
        if (o.CopyFileOwner) flags += "O";
        if (o.CopyFileAuditing) flags += "U";
        return flags;
    }

    public static string DirCopyFlags(RobocopyOptions o)
    {
        var flags = "";
        if (o.CopyDirData) flags += "D";
        if (o.CopyDirAttributes) flags += "A";
        if (o.CopyDirTimestamps) flags += "T";
        return flags;
    }

    private static void AddSubdirectories(List<string> args, SubdirectoryMode mode)
    {
        switch (mode)
        {
            case SubdirectoryMode.All: args.Add("/E"); break;
            case SubdirectoryMode.NonEmpty: args.Add("/S"); break;
        }
    }

    private static List<string> CleanPatterns(IEnumerable<string> patterns) =>
        patterns.Select(p => p.Trim().Trim('"').Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
