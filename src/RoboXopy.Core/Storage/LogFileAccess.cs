using System.IO.Compression;
using System.Text;

namespace RoboXopy.Core.Storage;

/// <summary>Reads, compresses, and opens retained run logs while hiding the compressed file format from callers.</summary>
public static class LogFileAccess
{
    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using Stream content = IsCompressed(path) ? new GZipStream(stream, CompressionMode.Decompress) : stream;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>Compresses a completed log and returns its new path; leaves the original in place if compression fails.</summary>
    public static string Compress(string path)
    {
        if (!File.Exists(path) || IsCompressed(path)) return path;

        var compressed = path + ".gz";
        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(compressed, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                input.CopyTo(gzip);
            }

            File.Delete(path);
            return compressed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(compressed)) File.Delete(compressed); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return path;
        }
    }

    /// <summary>Provides a normal text file suitable for the system shell, extracting compressed logs to a temp location.</summary>
    public static string PrepareForShell(string path)
    {
        if (!IsCompressed(path)) return path;

        var directory = Path.Combine(Path.GetTempPath(), "RoboXopy", "logs");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(path));
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var extracted = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.Read);
        gzip.CopyTo(extracted);
        return output;
    }

    public static bool IsCompressed(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
}
