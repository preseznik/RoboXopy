using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RoboXopy.Core.Updates;

/// <summary>
/// A semantic version (major.minor.patch[-prerelease]) compared by SemVer 2.0 precedence. A leading "v" and any
/// "+build" metadata are accepted and ignored.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PrereleaseIdentifiers = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> PrereleaseIdentifiers { get; }
    public bool IsPrerelease => PrereleaseIdentifiers.Count > 0;

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a valid version.");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string[] prerelease = [];
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = s[(dash + 1)..].Split('.');
            s = s[..dash];
            foreach (var id in prerelease)
            {
                if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
            }
        }

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // A release ranks above any of its prereleases.
        if (!IsPrerelease) return other.IsPrerelease ? 1 : 0;
        if (!other.IsPrerelease) return -1;

        for (var i = 0; i < Math.Min(PrereleaseIdentifiers.Count, other.PrereleaseIdentifiers.Count); i++)
        {
            result = CompareIdentifier(PrereleaseIdentifiers[i], other.PrereleaseIdentifiers[i]);
            if (result != 0) return result;
        }

        return PrereleaseIdentifiers.Count.CompareTo(other.PrereleaseIdentifiers.Count);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = a.All(char.IsAsciiDigit);
        var bNumeric = b.All(char.IsAsciiDigit);
        if (aNumeric && bNumeric)
        {
            var x = a.TrimStart('0');
            var y = b.TrimStart('0');
            var byLength = x.Length.CompareTo(y.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
        }

        if (aNumeric) return -1;
        if (bNumeric) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PrereleaseIdentifiers));

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PrereleaseIdentifiers)}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);
    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) < 0;
    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) > 0;
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) <= 0;
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) >= 0;

    private static int Compare(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
