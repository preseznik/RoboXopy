using System.Text.Json;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Storage;

public sealed class SettingsStore(AppPaths paths)
{
    public AppSettings Load() => AtomicFile.ReadJson<AppSettings>(paths.SettingsFile) ?? new AppSettings();

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(settings, JsonDefaults.Options));
}
