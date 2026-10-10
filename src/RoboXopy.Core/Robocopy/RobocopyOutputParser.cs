using System.Globalization;
using System.Text.RegularExpressions;
using RoboXopy.Core.Models;

namespace RoboXopy.Core.Robocopy;

public enum OutputLineKind
{
    Text,
    Progress,
    File,
    Directory,
    Error,
}

public sealed record OutputLine(OutputLineKind Kind, string Text)
{
    public double? Percent { get; init; }
    public string? Status { get; init; }
    public string? Name { get; init; }
    public long? Size { get; init; }

    public bool IsExtra => Status?.Contains("EXTRA", StringComparison.OrdinalIgnoreCase) == true || Status?.StartsWith('*') == true;
}

/// <summary>
/// Parses robocopy console output. Parsing relies on layout (tabs, columns, error codes) rather than
/// English words, so it also works with localized robocopy builds.
/// </summary>
public static partial class RobocopyOutputParser
{
    [GeneratedRegex(@"^\s*(?<p>\d{1,3}(?:[.,]\d+)?)%\s*$")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"^\t(?<status>[^\t]*)\t\t?\s*(?<size>\d+(?:[.,]\d+)?(?:\s?[kmgt])?)\s*\t(?<name>.+)$")]
    private static partial Regex FileRegex();

    [GeneratedRegex(@"^\t(?<status>[^\t]*?)\s*(?<count>-?\d+)\t(?<name>.+)$")]
    private static partial Regex DirectoryRegex();

    [GeneratedRegex(@"\(0x[0-9A-Fa-f]{8}\)")]
    private static partial Regex ErrorRegex();

    [GeneratedRegex(@"^\s*-{20,}\s*$")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^\s*[^:\t]{1,40}?\s*:\s+(?<values>.+)$")]
    private static partial Regex SummaryRowRegex();

    [GeneratedRegex(@"(?<num>\d+(?:[.,]\d+)?)(?:\s(?<unit>[kmgt])(?=\s|$))?")]
    private static partial Regex SummaryValueRegex();

    public static OutputLine Parse(string line)
    {
        var progress = ProgressRegex().Match(line);
        if (progress.Success && TryParseNumber(progress.Groups["p"].Value, out var pct))
        {
            return new OutputLine(OutputLineKind.Progress, line) { Percent = pct };
        }

        if (ErrorRegex().IsMatch(line))
        {
            return new OutputLine(OutputLineKind.Error, line);
        }

        var file = FileRegex().Match(line);
        if (file.Success)
        {
            return new OutputLine(OutputLineKind.File, line)
            {
                Status = file.Groups["status"].Value.Trim(),
                Size = ParseSize(file.Groups["size"].Value),
                Name = file.Groups["name"].Value.Trim(),
            };
        }

        var dir = DirectoryRegex().Match(line);
        if (dir.Success)
        {
            return new OutputLine(OutputLineKind.Directory, line)
            {
                Status = dir.Groups["status"].Value.Trim(),
                Name = dir.Groups["name"].Value.Trim(),
            };
        }

        return new OutputLine(OutputLineKind.Text, line);
    }

    /// <summary>Parses the Dirs / Files / Bytes rows of robocopy's closing summary table.</summary>
    public static RobocopySummary? ParseSummary(IReadOnlyList<string> lines)
    {
        var lastSeparator = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (SeparatorRegex().IsMatch(lines[i]))
            {
                lastSeparator = i;
                break;
            }
        }

        if (lastSeparator < 0) return null;

        var rows = new List<long[]>();
        for (var i = lastSeparator + 1; i < lines.Count && rows.Count < 3; i++)
        {
            var match = SummaryRowRegex().Match(lines[i]);
            if (!match.Success) continue;

            var values = SummaryValueRegex().Matches(match.Groups["values"].Value)
                .Select(m => ParseSize(m.Value))
                .ToArray();

            if (values.Length == 6) rows.Add(values);
        }

        if (rows.Count < 3) return null;

        return new RobocopySummary
        {
            Dirs = ToRow(rows[0]),
            Files = ToRow(rows[1]),
            Bytes = ToRow(rows[2]),
        };
    }

    /// <summary>Parses robocopy sizes such as <c>1234</c>, <c>1.5 m</c> or <c>12,3 g</c> into bytes.</summary>
    public static long ParseSize(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return 0;

        var multiplier = 1L;
        var last = char.ToLowerInvariant(text[^1]);
        if (last is 'k' or 'm' or 'g' or 't')
        {
            multiplier = last switch
            {
                'k' => 1L << 10,
                'm' => 1L << 20,
                'g' => 1L << 30,
                _ => 1L << 40,
            };
            text = text[..^1].Trim();
        }

        return TryParseNumber(text, out var value) ? (long)Math.Round(value * multiplier) : 0;
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static SummaryRow ToRow(long[] v) => new()
    {
        Total = v[0],
        Copied = v[1],
        Skipped = v[2],
        Mismatch = v[3],
        Failed = v[4],
        Extras = v[5],
    };
}
