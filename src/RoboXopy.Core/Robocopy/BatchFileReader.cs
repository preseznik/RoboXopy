using System.Text;
using System.Text.RegularExpressions;

namespace RoboXopy.Core.Robocopy;

/// <summary>Something the user should know about an imported batch file or robocopy command.</summary>
public sealed record ImportNote(IssueSeverity Severity, string Message);

/// <summary>A robocopy invocation found in a batch file.</summary>
/// <param name="LineNumber">1-based line the command starts on.</param>
/// <param name="RawText">The line as written, with <c>^</c> continuations joined.</param>
/// <param name="Text">The robocopy command with variables expanded and batch syntax removed.</param>
/// <param name="WorkingDirectory">The folder relative paths resolve against, when known.</param>
public sealed record BatchCommand(int LineNumber, string RawText, string Text, string? WorkingDirectory, IReadOnlyList<ImportNote> Notes);

public sealed record BatchReadResult(string? Path, IReadOnlyList<BatchCommand> Commands);

/// <summary>
/// Finds robocopy commands in a .bat/.cmd file. Understands the batch syntax people commonly wrap robocopy in:
/// <c>^</c> continuations, <c>set</c> variables, <c>%~dp0</c>, <c>cd</c>/<c>pushd</c>, <c>call</c>/<c>start</c>,
/// <c>if</c>/<c>for</c> wrappers, command chaining and output redirection.
/// </summary>
public static partial class BatchFileReader
{
    public const int MaxFileBytes = 1024 * 1024;

    private static readonly HashSet<string> IgnoredKeywords = new(StringComparer.Ordinal)
    {
        "echo", "title", "pause", "exit", "goto", "cls", "chcp", "color", "mode", "endlocal", "timeout", "prompt", "shift", "choice",
    };

    private static readonly HashSet<string> DynamicVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATE", "TIME", "RANDOM", "ERRORLEVEL", "CMDCMDLINE", "CMDEXTVERSION", "HIGHESTNUMANODENUMBER",
    };

    public static BatchReadResult Read(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"{info.Name} is larger than 1 MB, which is too big to be a batch file.");
        }

        return Parse(Decode(File.ReadAllBytes(fullPath)), fullPath);
    }

    /// <summary>Decodes using the BOM if present, otherwise UTF-8 when valid, otherwise the console's OEM code page (as cmd.exe does).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes is [0xFE, 0xFF, ..]) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return RobocopyRunner.OemEncoding.GetString(bytes);
        }
    }

    public static BatchReadResult Parse(string text, string? batchPath = null)
    {
        var fullPath = string.IsNullOrEmpty(batchPath) ? null : System.IO.Path.GetFullPath(batchPath);
        var state = new State(fullPath);
        var commands = new List<BatchCommand>();
        var blockDepth = 0;
        string? blockHeader = null;

        foreach (var (lineNumber, rawLine) in LogicalLines(text))
        {
            var line = rawLine.Trim().TrimStart('@').Trim();
            if (line.Length == 0 || line[0] == ':' || Keyword(line) == "rem") continue;

            var depthAtStart = blockDepth;
            var (opens, closes) = CountParens(line);
            blockDepth = Math.Max(0, blockDepth + opens - closes);
            if (opens > closes) blockHeader = line;

            var segments = SplitSegments(line);
            var found = new List<(string Text, Extraction Extraction, List<ImportNote> Notes)>();
            var ignored = new List<string>();

            foreach (var segment in segments)
            {
                var raw = segment.Trim().TrimStart('@').Trim();
                if (raw.Length == 0) continue;
                if (Keyword(raw) == "rem")
                {
                    break;
                }

                var notes = new List<ImportNote>();
                var expanded = state.Expand(raw, notes);
                var keyword = Keyword(expanded.TrimStart('(', ' ', '\t'));
                switch (keyword)
                {
                    case "set":
                        state.Set(expanded.TrimStart('(', ' ', '\t'), notes);
                        continue;
                    case "setlocal":
                        state.SetLocal(expanded);
                        continue;
                    case "cd" or "chdir":
                        state.ChangeDirectory(Arguments(expanded.TrimStart('(', ' ', '\t'), keyword), push: false);
                        continue;
                    case "pushd":
                        state.ChangeDirectory(Arguments(expanded.TrimStart('(', ' ', '\t'), keyword), push: true);
                        continue;
                    case "popd":
                        state.PopDirectory();
                        continue;
                }

                if (!IgnoredKeywords.Contains(keyword) && Extract(expanded) is { } extraction)
                {
                    found.Add((raw, extraction, notes));
                }
                else
                {
                    ignored.Add(raw);
                }
            }

            foreach (var (segmentText, extraction, notes) in found)
            {
                var others = found.Where(f => !ReferenceEquals(f.Extraction, extraction)).Select(f => f.Text).Concat(ignored).ToList();
                if (extraction.Remainder is { Length: > 0 } remainder) others.Add(remainder);

                AddExtractionNotes(extraction, notes, others, depthAtStart > 0 ? blockHeader : null);
                commands.Add(new BatchCommand(
                    lineNumber,
                    rawLine.Trim(),
                    extraction.CommandText,
                    state.WorkingDirectory,
                    notes.Distinct().ToList()));
            }
        }

        return new BatchReadResult(fullPath, commands);
    }

    /// <summary>True when <paramref name="token"/> names robocopy (optionally with a path and/or .exe).</summary>
    public static bool IsRobocopy(string token)
    {
        var name = token.Replace("\"", "").Trim();
        if (name.Length == 0) return false;
        var file = System.IO.Path.GetFileName(name);
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
        return file.Equals("robocopy", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExtractionNotes(Extraction extraction, List<ImportNote> notes, List<string> others, string? blockHeader)
    {
        var prefix = extraction.Prefix.Where(p => p is not "(" and not ")").ToList();
        var prefixText = string.Join(' ', prefix);
        var isConditional = prefix.Any(p => p.Equals("if", StringComparison.OrdinalIgnoreCase)
                                            || p.Equals("for", StringComparison.OrdinalIgnoreCase)
                                            || p.Equals("else", StringComparison.OrdinalIgnoreCase));

        if (isConditional)
        {
            notes.Add(new(IssueSeverity.Warning,
                $"In the batch file, robocopy runs as part of \"{prefixText}\". That condition wasn't imported; the profile always runs."));
        }
        else if (blockHeader is not null)
        {
            notes.Add(new(IssueSeverity.Warning,
                $"In the batch file, robocopy runs inside \"{blockHeader.TrimEnd('(', ' ')}\". That condition wasn't imported; the profile always runs."));
        }
        else if (prefix.Count > 0 && prefix[0].Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add(new(IssueSeverity.Info, "The start command around robocopy was ignored. RoboXopy runs robocopy and waits for it to finish."));
        }
        else if (prefix.Count > 0 && !(prefix.Count == 1 && prefix[0].Equals("call", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add(new(IssueSeverity.Info, $"\"{prefixText}\" before robocopy was ignored."));
        }

        if (extraction.Redirections.Count > 0)
        {
            notes.Add(new(IssueSeverity.Info,
                $"Output redirection ({string.Join(' ', extraction.Redirections)}) was ignored. RoboXopy shows and keeps robocopy's output itself."));
        }

        if (others.Count > 0)
        {
            notes.Add(new(IssueSeverity.Info,
                $"Other commands on the same line were ignored: {string.Join(", ", others.Select(o => $"\"{o.Trim()}\""))}."));
        }
    }

    /// <summary>Joins physical lines ending in an unescaped <c>^</c> with the next line.</summary>
    private static IEnumerable<(int LineNumber, string Text)> LogicalLines(string text)
    {
        var lines = text.Replace("\0", "").TrimEnd('\x1A').Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var current = new StringBuilder();
        var start = 1;
        var continuing = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!continuing) start = i + 1;
            var line = lines[i];
            var carets = 0;
            while (carets < line.Length && line[line.Length - 1 - carets] == '^') carets++;

            var inQuotes = (current.ToString().Count(c => c == '"') + line.Count(c => c == '"')) % 2 == 1;
            if (carets % 2 == 1 && !inQuotes && i < lines.Length - 1)
            {
                current.Append(line, 0, line.Length - 1);
                continuing = true;
                continue;
            }

            current.Append(line);
            yield return (start, current.ToString());
            current.Clear();
            continuing = false;
        }
    }

    /// <summary>Splits a line at unquoted, unescaped <c>&amp;</c>, <c>&amp;&amp;</c>, <c>|</c> and <c>||</c>.</summary>
    private static List<string> SplitSegments(string line)
    {
        var segments = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if (!inQuotes && c == '^' && i + 1 < line.Length)
            {
                sb.Append(c).Append(line[++i]);
            }
            else if (!inQuotes && c is '&' or '|')
            {
                if (c == '&' && sb.Length > 0 && sb[^1] is '>' or '<')
                {
                    sb.Append(c);
                    continue;
                }

                segments.Add(sb.ToString());
                sb.Clear();
                if (i + 1 < line.Length && line[i + 1] == c) i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        segments.Add(sb.ToString());
        return segments;
    }

    private static (int Opens, int Closes) CountParens(string line)
    {
        int opens = 0, closes = 0;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '"': inQuotes = !inQuotes; break;
                case '^' when !inQuotes: i++; break;
                case '(' when !inQuotes: opens++; break;
                case ')' when !inQuotes: closes++; break;
            }
        }

        return (opens, closes);
    }

    private static string Keyword(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
        return text[..i].ToLowerInvariant();
    }

    private static string Arguments(string text, string keyword) => text.Length > keyword.Length ? text[keyword.Length..].Trim() : "";

    private sealed record Extraction(List<string> Prefix, string Executable, List<string> Arguments, List<string> Redirections, string? Remainder)
    {
        public string CommandText => Arguments.Count == 0 ? Executable : Executable + " " + string.Join(' ', Arguments);
    }

    /// <summary>Finds robocopy in one command segment and returns its arguments with carets, redirections and wrappers removed.</summary>
    private static Extraction? Extract(string segment)
    {
        var prefix = new List<string>();
        var arguments = new List<string>();
        var redirections = new List<string>();
        string? executable = null;
        string? remainder = null;
        var depth = 0;
        var word = new StringBuilder();
        var hasWord = false;
        var inQuotes = false;

        void Flush()
        {
            if (!hasWord) return;
            var w = word.ToString();
            word.Clear();
            hasWord = false;
            if (executable is not null)
            {
                arguments.Add(w);
            }
            else if (IsRobocopy(w) && !(prefix.Count > 0 && prefix[^1].Equals("exist", StringComparison.OrdinalIgnoreCase)))
            {
                executable = w;
            }
            else
            {
                prefix.Add(w);
            }
        }

        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            if (inQuotes)
            {
                word.Append(c);
                if (c == '"') inQuotes = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    word.Append(c);
                    hasWord = true;
                    break;
                case '^':
                    if (i + 1 < segment.Length)
                    {
                        word.Append(segment[++i]);
                        hasWord = true;
                    }

                    break;
                case ' ' or '\t':
                    Flush();
                    break;
                case '<' or '>':
                    i = ReadRedirection(segment, i, word, ref hasWord, redirections, Flush);
                    break;
                case '(' when executable is null && !hasWord:
                    depth++;
                    prefix.Add("(");
                    break;
                case ')' when executable is null:
                    Flush();
                    depth = Math.Max(0, depth - 1);
                    prefix.Add(")");
                    break;
                case ')' when depth > 0:
                    Flush();
                    remainder = segment[(i + 1)..].Trim();
                    i = segment.Length;
                    break;
                default:
                    word.Append(c);
                    hasWord = true;
                    break;
            }
        }

        Flush();
        return executable is null ? null : new Extraction(prefix, executable, arguments, redirections, remainder);
    }

    private static int ReadRedirection(string s, int i, StringBuilder word, ref bool hasWord, List<string> redirections, Action flush)
    {
        var redirect = new StringBuilder();
        if (hasWord && word.Length == 1 && char.IsAsciiDigit(word[0]))
        {
            redirect.Append(word[0]);
            word.Clear();
            hasWord = false;
        }
        else
        {
            flush();
        }

        var op = s[i];
        redirect.Append(op);
        if (op == '>' && i + 1 < s.Length && s[i + 1] == '>')
        {
            redirect.Append('>');
            i++;
        }

        if (i + 1 < s.Length && s[i + 1] == '&')
        {
            redirect.Append('&');
            i++;
            while (i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])) redirect.Append(s[++i]);
        }
        else
        {
            while (i + 1 < s.Length && s[i + 1] is ' ' or '\t') i++;
            var target = new StringBuilder();
            var quoted = false;
            while (i + 1 < s.Length)
            {
                var n = s[i + 1];
                if (!quoted && n is ' ' or '\t' or '<' or '>' or ')') break;
                if (n == '"') quoted = !quoted;
                target.Append(n);
                i++;
            }

            if (target.Length > 0) redirect.Append(' ').Append(target);
        }

        redirections.Add(redirect.ToString());
        return i;
    }

    [GeneratedRegex(@"\G%~([fdpnxsatz]*)(\$[^:%]+:)?([0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ArgumentModifierRegex();

    [GeneratedRegex(@"!([^!\s][^!]*)!")]
    private static partial Regex DelayedVariableRegex();

    /// <summary>Variables, working directory and delayed-expansion state as the batch file runs top to bottom.</summary>
    private sealed class State(string? batchPath)
    {
        private readonly Dictionary<string, (string Value, List<ImportNote> Notes)> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runtimeVariables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<string?> _directories = new();
        private bool _delayedExpansion;

        public string? WorkingDirectory { get; private set; } = batchPath is null ? null : System.IO.Path.GetDirectoryName(batchPath);

        public string Expand(string text, List<ImportNote> notes)
        {
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '%')
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= text.Length) break;
                var next = text[i + 1];
                if (next == '%')
                {
                    sb.Append('%');
                    i++;
                }
                else if (char.IsAsciiDigit(next))
                {
                    sb.Append(Argument(next - '0', "", $"%{next}", notes));
                    i++;
                }
                else if (next == '*')
                {
                    notes.Add(ArgumentNote("%*"));
                    sb.Append("%*");
                    i++;
                }
                else if (next == '~' && ArgumentModifierRegex().Match(text, i) is { Success: true } m)
                {
                    var modifiers = m.Groups[2].Success ? null : m.Groups[1].Value;
                    sb.Append(Argument(m.Groups[3].Value[0] - '0', modifiers, m.Value, notes));
                    i += m.Length - 1;
                }
                else
                {
                    var close = text.IndexOf('%', i + 1);
                    if (close < 0) continue; // cmd.exe drops a lone percent sign

                    var body = text[(i + 1)..close];
                    var colon = body.IndexOf(':');
                    sb.Append(Variable(colon < 0 ? body : body[..colon], colon < 0 ? null : body[(colon + 1)..], text[i..(close + 1)], notes));
                    i = close;
                }
            }

            var result = sb.ToString();
            if (_delayedExpansion)
            {
                result = DelayedVariableRegex().Replace(result, m =>
                {
                    var body = m.Groups[1].Value;
                    var colon = body.IndexOf(':');
                    var value = Variable(colon < 0 ? body : body[..colon], colon < 0 ? null : body[(colon + 1)..], m.Value, notes);
                    return value == m.Value && Environment.GetEnvironmentVariable(body) is not null ? $"%{body}%" : value;
                });
            }

            return result;
        }

        private string Variable(string name, string? operation, string literal, List<ImportNote> notes)
        {
            string value;
            if (name.Length == 0) return literal;
            if (_variables.TryGetValue(name, out var known))
            {
                value = known.Value;
                notes.AddRange(known.Notes);
            }
            else if (_runtimeVariables.Contains(name))
            {
                notes.Add(new(IssueSeverity.Warning, $"{literal} is calculated or typed in while the batch file runs, so it was left as is."));
                return literal;
            }
            else if (name.Equals("CD", StringComparison.OrdinalIgnoreCase) && WorkingDirectory is not null)
            {
                value = WorkingDirectory;
                notes.Add(new(IssueSeverity.Info, $"{literal} was taken to be {WorkingDirectory}."));
            }
            else if (DynamicVariables.Contains(name))
            {
                notes.Add(new(IssueSeverity.Warning, $"{literal} changes every time the batch file runs, so it was left as is."));
                return literal;
            }
            else if (Environment.GetEnvironmentVariable(name) is { } environmentValue)
            {
                if (operation is null) return literal; // RoboXopy expands environment variables in paths when the profile runs
                value = environmentValue;
            }
            else
            {
                notes.Add(new(IssueSeverity.Warning, $"{literal} isn't set in the batch file or in your environment, so it was left as is."));
                return literal;
            }

            if (operation is null) return value;
            if (ApplyOperation(value, operation) is { } result) return result;

            notes.Add(new(IssueSeverity.Warning, $"Couldn't work out {literal}, so it was left as is."));
            return literal;
        }

        /// <summary>Implements <c>%VAR:~start,length%</c> and <c>%VAR:find=replace%</c>.</summary>
        private static string? ApplyOperation(string value, string operation)
        {
            if (operation.StartsWith('~'))
            {
                var parts = operation[1..].Split(',');
                if (parts.Length > 2 || !int.TryParse(parts[0], out var start)) return null;
                int? length = null;
                if (parts.Length == 2)
                {
                    if (!int.TryParse(parts[1], out var l)) return null;
                    length = l;
                }

                if (start < 0) start = Math.Max(0, value.Length + start);
                var rest = value[Math.Min(start, value.Length)..];
                return length switch
                {
                    null => rest,
                    < 0 => rest[..Math.Max(0, rest.Length + length.Value)],
                    _ => rest[..Math.Min(length.Value, rest.Length)],
                };
            }

            var equals = operation.IndexOf('=');
            if (equals < 0) return null;
            var find = operation[..equals];
            var replacement = operation[(equals + 1)..];
            if (find.StartsWith('*'))
            {
                find = find[1..];
                if (find.Length == 0) return null;
                var index = value.IndexOf(find, StringComparison.OrdinalIgnoreCase);
                return index < 0 ? value : replacement + value[(index + find.Length)..];
            }

            return find.Length == 0 ? null : value.Replace(find, replacement, StringComparison.OrdinalIgnoreCase);
        }

        private string Argument(int index, string? modifiers, string literal, List<ImportNote> notes)
        {
            if (index != 0 || batchPath is null || modifiers is null || modifiers.IndexOfAny(['s', 'a', 't', 'z', 'S', 'A', 'T', 'Z']) >= 0)
            {
                notes.Add(index == 0 && batchPath is not null
                    ? new(IssueSeverity.Warning, $"{literal} isn't supported, so it was left as is.")
                    : ArgumentNote(literal));
                return literal;
            }

            var m = modifiers.ToLowerInvariant();
            if (m.Length == 0) return batchPath;
            if (m.Contains('f')) m = "dpnx";

            var root = System.IO.Path.GetPathRoot(batchPath) ?? "";
            var drive = root.TrimEnd('\\', '/');
            var directory = System.IO.Path.GetDirectoryName(batchPath) ?? root;
            if (!directory.EndsWith('\\')) directory += "\\";

            var sb = new StringBuilder();
            if (m.Contains('d')) sb.Append(drive);
            if (m.Contains('p')) sb.Append(directory[drive.Length..]);
            if (m.Contains('n')) sb.Append(System.IO.Path.GetFileNameWithoutExtension(batchPath));
            if (m.Contains('x')) sb.Append(System.IO.Path.GetExtension(batchPath));
            return sb.ToString();
        }

        private static ImportNote ArgumentNote(string literal) =>
            new(IssueSeverity.Warning, $"{literal} is a value passed to the batch file when it runs, so it was left as is.");

        public void Set(string text, List<ImportNote> notes)
        {
            var rest = text.Length > 3 ? text[3..].Trim() : "";
            if (rest.StartsWith("/a", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("/p", StringComparison.OrdinalIgnoreCase))
            {
                var assignment = rest[2..].Trim().Trim('"');
                var eq = assignment.IndexOf('=');
                if (eq > 0)
                {
                    var name = assignment[..eq].Trim().TrimEnd('+', '-', '*', '/', '%', '&', '|', '^', '<', '>').Trim();
                    _variables.Remove(name);
                    _runtimeVariables.Add(name);
                }

                return;
            }

            string body;
            if (rest.StartsWith('"'))
            {
                var end = rest.LastIndexOf('"');
                body = end > 0 ? rest[1..end] : rest[1..];
            }
            else
            {
                body = Unescape(rest).TrimEnd();
            }

            var equals = body.IndexOf('=');
            if (equals <= 0) return;

            var variable = body[..equals].Trim();
            var value = body[(equals + 1)..];
            _runtimeVariables.Remove(variable);
            if (value.Length == 0)
            {
                _variables.Remove(variable);
            }
            else
            {
                _variables[variable] = (value, notes.ToList());
            }
        }

        public void SetLocal(string text)
        {
            if (text.Contains("enabledelayedexpansion", StringComparison.OrdinalIgnoreCase)) _delayedExpansion = true;
            else if (text.Contains("disabledelayedexpansion", StringComparison.OrdinalIgnoreCase)) _delayedExpansion = false;
        }

        public void ChangeDirectory(string argument, bool push)
        {
            var path = argument;
            if (path.StartsWith("/d", StringComparison.OrdinalIgnoreCase) && (path.Length == 2 || char.IsWhiteSpace(path[2])))
            {
                path = path[2..];
            }

            path = Unescape(path).Trim().Trim('"').Trim();
            if (path.Length == 0) return;
            if (push) _directories.Push(WorkingDirectory);

            WorkingDirectory = Resolve(path, WorkingDirectory);
        }

        public void PopDirectory()
        {
            if (_directories.Count > 0) WorkingDirectory = _directories.Pop();
        }

        private static string? Resolve(string path, string? baseDirectory)
        {
            if (path.Contains('%')) return null;
            try
            {
                if (System.IO.Path.IsPathFullyQualified(path)) return System.IO.Path.GetFullPath(path);
                return baseDirectory is not null && System.IO.Path.IsPathFullyQualified(baseDirectory)
                    ? System.IO.Path.GetFullPath(path, baseDirectory)
                    : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>Removes cmd.exe caret escapes outside quotes, stopping at an unescaped command separator.</summary>
        private static string Unescape(string text)
        {
            var sb = new StringBuilder(text.Length);
            var inQuotes = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"') inQuotes = !inQuotes;
                if (!inQuotes && c == '^' && i + 1 < text.Length)
                {
                    sb.Append(text[++i]);
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
