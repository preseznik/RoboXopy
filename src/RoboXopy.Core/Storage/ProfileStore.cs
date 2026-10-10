using System.Text.Json;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Storage;

/// <summary>Stores each profile as its own JSON file under profiles\.</summary>
public sealed class ProfileStore(AppPaths paths)
{
    public IReadOnlyList<SyncProfile> LoadAll()
    {
        if (!Directory.Exists(paths.ProfilesDirectory)) return [];

        var profiles = new List<SyncProfile>();
        foreach (var file in Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json"))
        {
            var profile = AtomicFile.ReadJson<SyncProfile>(file);
            if (profile is not null) profiles.Add(profile);
        }

        return profiles.OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt).ToList();
    }

    public SyncProfile? Load(Guid id) => AtomicFile.ReadJson<SyncProfile>(paths.ProfileFile(id));

    /// <summary>Finds a profile by id, short id prefix, or (case-insensitive) name.</summary>
    public SyncProfile? Find(string idOrName)
    {
        if (Guid.TryParse(idOrName, out var id)) return Load(id);

        var all = LoadAll();
        return all.FirstOrDefault(p => p.Id.ToString("N").StartsWith(idOrName, StringComparison.OrdinalIgnoreCase) && idOrName.Length >= 6)
               ?? all.FirstOrDefault(p => string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(SyncProfile profile)
    {
        profile.ModifiedAt = DateTimeOffset.Now;
        AtomicFile.WriteAllText(paths.ProfileFile(profile.Id), JsonSerializer.Serialize(profile, JsonDefaults.Options));
    }

    public void Delete(Guid id)
    {
        var file = paths.ProfileFile(id);
        if (File.Exists(file)) File.Delete(file);
    }
}
