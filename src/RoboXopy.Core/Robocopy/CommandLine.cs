using System.Text;

namespace RoboXopy.Core.Robocopy;

/// <summary>Windows command-line quoting/splitting helpers (MSVCRT argv rules, as used by robocopy).</summary>
public static class CommandLine
{
    public static string Quote(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        if (argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return argument;

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>Splits free-form user text into arguments, honouring double quotes.</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) result.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken) result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// Trims whitespace/quotes and a trailing separator (except for roots such as <c>C:\</c>), which
    /// avoids robocopy's notorious <c>"C:\path\"</c> quoting problem.
    /// </summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim().Trim('"').Trim();
        p = Environment.ExpandEnvironmentVariables(p);
        if (p.Length == 2 && p[1] == ':') return p + "\\";

        while (p.Length > 3 && (p.EndsWith('\\') || p.EndsWith('/')))
        {
            p = p[..^1];
        }

        return p;
    }
}
