using System.Text.Json;

namespace RoboXopy.Core.Storage;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    public static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
