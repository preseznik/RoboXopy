using System.Text;
using System.Text.Json;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Storage;

/// <summary>
/// Append-only JSON-lines run history (one file per profile). Safe to append from several
/// processes – e.g. the tray app and a Task Scheduler launched instance.
/// </summary>
public sealed class HistoryStore(AppPaths paths)
{
    public const int MaxRecordsPerProfile = 500;

    public void Append(RunRecord record)
    {
        Directory.CreateDirectory(paths.HistoryDirectory);
        var line = JsonSerializer.Serialize(record, JsonDefaults.Compact) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);
        var file = paths.HistoryFile(record.ProfileId);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
                stream.Write(bytes);
                break;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(50);
            }
        }

        TrimIfNeeded(record.ProfileId);
    }

    /// <summary>Returns runs newest first.</summary>
    public IReadOnlyList<RunRecord> Load(Guid profileId, int max = MaxRecordsPerProfile)
    {
        var file = paths.HistoryFile(profileId);
        if (!File.Exists(file)) return [];

        var records = new List<RunRecord>();
        foreach (var line in ReadLinesShared(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<RunRecord>(line, JsonDefaults.Options);
                if (record is not null) records.Add(record);
            }
            catch (JsonException)
            {
                // Ignore a partially written line.
            }
        }

        records.Reverse();
        return records.Count > max ? records.Take(max).ToList() : records;
    }

    public RunRecord? LoadLatest(Guid profileId) => Load(profileId, 1).FirstOrDefault();

    public void Clear(Guid profileId)
    {
        var file = paths.HistoryFile(profileId);
        if (File.Exists(file)) File.Delete(file);
    }

    public void DeleteProfileData(Guid profileId)
    {
        Clear(profileId);
        var logs = paths.ProfileLogsDirectory(profileId);
        if (Directory.Exists(logs))
        {
            try { Directory.Delete(logs, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes the oldest log files so that at most <paramref name="keep"/> remain.</summary>
    public void ApplyLogRetention(Guid profileId, int keep)
    {
        if (keep <= 0) return;
        var dir = paths.ProfileLogsDirectory(profileId);
        if (!Directory.Exists(dir)) return;

        foreach (var file in new DirectoryInfo(dir).GetFiles("*.log").OrderByDescending(f => f.CreationTimeUtc).Skip(keep))
        {
            try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void TrimIfNeeded(Guid profileId)
    {
        var file = paths.HistoryFile(profileId);
        try
        {
            var info = new FileInfo(file);
            if (info.Length < 256 * 1024) return;

            var lines = ReadLinesShared(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count <= MaxRecordsPerProfile) return;
            AtomicFile.WriteAllText(file, string.Join(Environment.NewLine, lines.Skip(lines.Count - MaxRecordsPerProfile)) + Environment.NewLine);
        }
        catch (IOException)
        {
            // Another process is busy with the file; try again next time.
        }
    }

    private static List<string> ReadLinesShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }
}
