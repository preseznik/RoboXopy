using System.Text.Json;

namespace RoboXopy.Core.Updates;

/// <summary>A file attached to a release.</summary>
/// <param name="Sha256">Lower-case hex SHA-256 published by GitHub for the file, when available.</param>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

/// <summary>A published release, as returned by the GitHub releases API.</summary>
public sealed record ReleaseInfo(
    SemanticVersion Version,
    string Tag,
    string Title,
    string Notes,
    Uri PageUrl,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Parses one release object from the GitHub REST API.</summary>
    /// <returns>The release, or null for drafts and tags that aren't a version.</returns>
    public static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static ReleaseInfo? Parse(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(release, "draft")) return null;

        var tag = GetString(release, "tag_name");
        if (!SemanticVersion.TryParse(tag, out var version)) return null;
        if (!Uri.TryCreate(GetString(release, "html_url"), UriKind.Absolute, out var page)) return null;

        var assets = new List<ReleaseAsset>();
        if (release.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                var name = GetString(asset, "name");
                if (string.IsNullOrEmpty(name)) continue;
                if (!Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var url)) continue;
                var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                assets.Add(new ReleaseAsset(name, url, size, ParseDigest(GetString(asset, "digest"))));
            }
        }

        DateTimeOffset? published = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String &&
                                    p.TryGetDateTimeOffset(out var date)
            ? date
            : null;

        var title = GetString(release, "name");
        return new ReleaseInfo(
            version,
            tag!,
            string.IsNullOrWhiteSpace(title) ? tag! : title.Trim(),
            GetString(release, "body") ?? "",
            page,
            published,
            GetBool(release, "prerelease"),
            assets);
    }

    /// <summary>The installer for the given runtime, e.g. RoboXopy-1.2.0-win-x64-setup.exe.</summary>
    public ReleaseAsset? FindInstaller(string runtime)
    {
        var expected = $"RoboXopy-{Version}-{runtime}-setup.exe";
        return Assets.FirstOrDefault(a => string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase))
            ?? Assets.FirstOrDefault(a => a.Name.EndsWith($"-{runtime}-setup.exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The "&lt;file&gt;.sha256" checksum file published next to an asset.</summary>
    public ReleaseAsset? FindChecksumFile(ReleaseAsset asset) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a SHA-256 from a checksum file in the common "&lt;hash&gt;  &lt;file name&gt;" format (one or more lines). A
    /// line without a file name applies to any file.
    /// </summary>
    public static string? ParseChecksumFile(string content, string fileName)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var space = line.IndexOfAny([' ', '\t']);
            var hash = space < 0 ? line : line[..space];
            var name = space < 0 ? "" : line[space..].Trim().TrimStart('*');
            if (!IsSha256(hash)) continue;
            if (name.Length == 0 || string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
        }

        return null;
    }

    internal static string? ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hash = digest[prefix.Length..];
        return IsSha256(hash) ? hash.ToLowerInvariant() : null;
    }

    internal static bool IsSha256(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
