using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Review;

/// <summary>
/// Parses git unified diff text (as produced by git diff or the GitHub diff media type) into files, hunks and lines.
/// Tolerant: unknown header lines are skipped, a broken hunk never throws.
/// </summary>
public static class UnifiedDiffParser
{
    /// <summary>Default cap for the patch of one file, in characters. Larger patches are skipped (IsTooLarge).</summary>
    public const int DefaultMaxPatchChars = 400_000;

    private static readonly Regex HunkHeader = new(
        @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<DiffFile> Parse(string? diffText, int maxPatchChars = DefaultMaxPatchChars)
    {
        var files = new List<DiffFile>();
        if (string.IsNullOrEmpty(diffText)) return files;

        var lines = diffText.Split('\n');
        var count = lines.Length;
        // A trailing newline produces one empty last element; it is not a content line.
        if (count > 0 && lines[count - 1].Length == 0) count--;

        FileBuilder? file = null;
        HunkBuilder? hunk = null;
        var oldRemaining = 0;
        var newRemaining = 0;

        void FlushHunk()
        {
            if (file is not null && hunk is not null) file.AddHunk(hunk);
            hunk = null;
            oldRemaining = 0;
            newRemaining = 0;
        }

        void FlushFile()
        {
            FlushHunk();
            if (file is not null) files.Add(file.Build());
            file = null;
        }

        for (var i = 0; i < count; i++)
        {
            var raw = lines[i];
            var line = raw.EndsWith('\r') ? raw.Substring(0, raw.Length - 1) : raw;

            // Inside a hunk: content lines are counted by the header numbers, so "--- x" inside a hunk is data.
            if (hunk is not null)
            {
                if (line.Length > 0 && line[0] == '\\')
                {
                    hunk.AddNoNewline(line);
                    continue;
                }

                var inside = oldRemaining > 0 || newRemaining > 0;
                if (inside)
                {
                    var first = line.Length == 0 ? ' ' : line[0];
                    if (first == ' ' || first == '+' || first == '-')
                    {
                        var text = line.Length == 0 ? string.Empty : line.Substring(1);
                        if (first == ' ')
                        {
                            hunk.AddContext(text);
                            oldRemaining--;
                            newRemaining--;
                        }
                        else if (first == '+')
                        {
                            hunk.AddAdd(text);
                            newRemaining--;
                        }
                        else
                        {
                            hunk.AddRemove(text);
                            oldRemaining--;
                        }
                        file?.CountLine(first, maxPatchChars, text.Length);
                        continue;
                    }
                }
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                file = new FileBuilder();
                file.SetFromDiffGit(line.Substring("diff --git ".Length));
                continue;
            }

            var match = HunkHeader.Match(line);
            if (match.Success)
            {
                file ??= new FileBuilder();
                FlushHunk();
                var oldStart = ToInt(match.Groups[1].Value, 0);
                var oldCount = match.Groups[2].Success ? ToInt(match.Groups[2].Value, 1) : 1;
                var newStart = ToInt(match.Groups[3].Value, 0);
                var newCount = match.Groups[4].Success ? ToInt(match.Groups[4].Value, 1) : 1;
                hunk = new HunkBuilder(line, oldStart, oldCount, newStart, newCount);
                oldRemaining = oldCount;
                newRemaining = newCount;
                continue;
            }

            if (file is null)
            {
                // Plain "--- a/x" / "+++ b/x" diffs without "diff --git" lines.
                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    file = new FileBuilder();
                    file.SetOld(line.Substring(4));
                }
                continue;
            }

            if (hunk is not null)
            {
                // A header line after a finished hunk: plain unified diffs start the next file with "--- ".
                if (line.StartsWith("--- ", StringComparison.Ordinal) && i + 1 < count && lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
                {
                    FlushFile();
                    file = new FileBuilder();
                    file.SetOld(line.Substring(4));
                }
                continue;
            }

            file.ApplyHeader(line);
        }

        FlushFile();
        return files;
    }

    private static int ToInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    /// <summary>Removes git's C-style quoting (quotes, backslash escapes, octal UTF-8 bytes) from a path.</summary>
    public static string Unquote(string path)
    {
        var p = path.Trim();
        if (p.Length < 2 || p[0] != '"' || p[p.Length - 1] != '"') return p;
        var inner = p.Substring(1, p.Length - 2);
        var bytes = new List<byte>();
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c != '\\' || i + 1 >= inner.Length)
            {
                AppendChar(bytes, c);
                continue;
            }

            var n = inner[++i];
            switch (n)
            {
                case 't': bytes.Add((byte)'\t'); break;
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case '"': bytes.Add((byte)'"'); break;
                case '\\': bytes.Add((byte)'\\'); break;
                default:
                    if (n >= '0' && n <= '7')
                    {
                        var value = n - '0';
                        var digits = 1;
                        while (digits < 3 && i + 1 < inner.Length && inner[i + 1] >= '0' && inner[i + 1] <= '7')
                        {
                            value = (value * 8) + (inner[++i] - '0');
                            digits++;
                        }
                        bytes.Add((byte)(value & 0xFF));
                    }
                    else
                    {
                        AppendChar(bytes, n);
                    }
                    break;
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void AppendChar(List<byte> bytes, char c)
    {
        if (c < 128) bytes.Add((byte)c);
        else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
    }

    internal static string StripPrefix(string path)
    {
        if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal)) return path.Substring(2);
        return path;
    }

    private sealed class HunkBuilder
    {
        private readonly List<DiffLine> _lines = new();
        private int _old;
        private int _new;

        public HunkBuilder(string header, int oldStart, int oldCount, int newStart, int newCount)
        {
            Header = header;
            OldStart = oldStart;
            OldCount = oldCount;
            NewStart = newStart;
            NewCount = newCount;
            _old = oldStart;
            _new = newStart;
        }

        public string Header { get; }
        public int OldStart { get; }
        public int OldCount { get; }
        public int NewStart { get; }
        public int NewCount { get; }
        public int LineCount => _lines.Count;

        public void AddContext(string text) => _lines.Add(new DiffLine(DiffLineKind.Context, _old++, _new++, text));

        public void AddAdd(string text) => _lines.Add(new DiffLine(DiffLineKind.Add, null, _new++, text));

        public void AddRemove(string text) => _lines.Add(new DiffLine(DiffLineKind.Remove, _old++, null, text));

        public void AddNoNewline(string text) => _lines.Add(new DiffLine(DiffLineKind.NoNewline, null, null, text));

        public DiffHunk Build() => new(Header, OldStart, OldCount, NewStart, NewCount, _lines);
    }

    private sealed class FileBuilder
    {
        private readonly List<DiffHunk> _hunks = new();
        private string _path = string.Empty;
        private string? _oldPath;
        private DiffFileStatus _status = DiffFileStatus.Modified;
        private bool _binary;
        private bool _tooLarge;
        private int _additions;
        private int _deletions;
        private long _chars;
        private bool _renameTo;

        public void SetFromDiffGit(string rest)
        {
            var (a, b) = SplitDiffGit(rest);
            _oldPath = StripPrefix(a);
            _path = StripPrefix(b);
            if (_oldPath == _path) _oldPath = null;
        }

        public void SetOld(string value)
        {
            var p = TrimTab(value);
            if (p == "/dev/null") return;
            _oldPath = StripPrefix(Unquote(p));
            if (_path.Length == 0) _path = _oldPath;
        }

        public void ApplyHeader(string line)
        {
            if (line.StartsWith("new file mode", StringComparison.Ordinal))
            {
                _status = DiffFileStatus.Added;
                _oldPath = null;
            }
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                _status = DiffFileStatus.Deleted;
                _oldPath = null;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                _status = DiffFileStatus.Renamed;
                _oldPath = Unquote(line.Substring("rename from ".Length));
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                _status = DiffFileStatus.Renamed;
                _renameTo = true;
                _path = Unquote(line.Substring("rename to ".Length));
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                _binary = true;
                if (line.StartsWith("Binary files /dev/null and ", StringComparison.Ordinal))
                {
                    _status = DiffFileStatus.Added;
                }
                else if (line.StartsWith("Binary files ", StringComparison.Ordinal) && line.EndsWith(" and /dev/null differ", StringComparison.Ordinal))
                {
                    _status = DiffFileStatus.Deleted;
                }
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                var p = TrimTab(line.Substring(4));
                if (p == "/dev/null") _status = DiffFileStatus.Added;
                else if (_path.Length == 0) _path = StripPrefix(Unquote(p));
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var p = TrimTab(line.Substring(4));
                if (p == "/dev/null")
                {
                    _status = DiffFileStatus.Deleted;
                }
                else if (!_renameTo)
                {
                    _path = StripPrefix(Unquote(p));
                }
            }
        }

        public void CountLine(char kind, int maxPatchChars, int textLength)
        {
            if (kind == '+') _additions++;
            else if (kind == '-') _deletions++;
            _chars += textLength + 1;
            if (_chars > maxPatchChars) _tooLarge = true;
        }

        public void AddHunk(HunkBuilder hunk)
        {
            if (_tooLarge) return;
            _hunks.Add(hunk.Build());
        }

        public DiffFile Build()
        {
            var path = _path;
            if (path.Length == 0 && _oldPath is not null) path = _oldPath;
            var status = _status;
            var hunks = _tooLarge ? new List<DiffHunk>() : _hunks;
            return new DiffFile(path, status == DiffFileStatus.Renamed ? _oldPath : null, status, _binary, _tooLarge, _additions, _deletions, hunks);
        }

        private static string TrimTab(string value)
        {
            var tab = value.IndexOf('\t');
            return (tab < 0 ? value : value.Substring(0, tab)).Trim();
        }

        private static (string A, string B) SplitDiffGit(string rest)
        {
            var text = rest.Trim();
            if (text.Length > 0 && text[0] == '"')
            {
                var end = FindClosingQuote(text, 0);
                if (end > 0)
                {
                    var first = Unquote(text.Substring(0, end + 1));
                    var second = Unquote(text.Substring(end + 1).Trim());
                    return (first, second);
                }
            }

            // Unquoted: "a/x y b/x y" is ambiguous, so look for the split where both halves name the same path.
            var candidates = new List<int>();
            var from = 0;
            while (true)
            {
                var idx = text.IndexOf(" b/", from, StringComparison.Ordinal);
                if (idx < 0) break;
                candidates.Add(idx);
                from = idx + 1;
            }

            var quotedSecond = text.IndexOf(" \"b/", StringComparison.Ordinal);
            if (quotedSecond > 0) candidates.Add(quotedSecond);

            foreach (var idx in candidates)
            {
                var left = text.Substring(0, idx);
                var right = Unquote(text.Substring(idx + 1));
                if (StripPrefix(left) == StripPrefix(right)) return (left, right);
            }

            if (candidates.Count > 0)
            {
                var idx = candidates[candidates.Count - 1];
                return (text.Substring(0, idx), Unquote(text.Substring(idx + 1)));
            }

            var space = text.IndexOf(' ');
            return space < 0 ? (text, text) : (text.Substring(0, space), text.Substring(space + 1));
        }

        private static int FindClosingQuote(string text, int start)
        {
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') i++;
                else if (text[i] == '"') return i;
            }
            return -1;
        }
    }
}
