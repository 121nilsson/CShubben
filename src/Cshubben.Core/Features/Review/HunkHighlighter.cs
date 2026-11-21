using System.Runtime.CompilerServices;
using TextMateSharp.Grammars;

namespace Cshubben.Core.Features.Review;

/// <summary>
/// Highlights every line of a hunk with grammar state carried from line to line, so block comments, raw strings
/// and template literals colour correctly. The old and the new side are tokenized as two separate streams.
/// A hunk is assumed to start outside any multi-line construct, which a diff cannot tell us otherwise.
/// </summary>
public static class HunkHighlighter
{
    private const int MaxLineLength = 400;

    private static readonly ConditionalWeakTable<DiffHunk, Dictionary<string, IReadOnlyList<CodeToken>[]>> Cache = new();

    /// <summary>The tokens for one line of the hunk. Falls back to stateless highlighting for an out-of-range index.</summary>
    public static IReadOnlyList<CodeToken> TokensFor(DiffHunk hunk, int lineIndex, string path)
    {
        var all = Cache.GetValue(hunk, _ => new Dictionary<string, IReadOnlyList<CodeToken>[]>(StringComparer.Ordinal));
        IReadOnlyList<CodeToken>[] lines;
        lock (all)
        {
            if (!all.TryGetValue(path, out var cached))
            {
                cached = Compute(hunk, path);
                all[path] = cached;
            }

            lines = cached;
        }

        return lineIndex >= 0 && lineIndex < lines.Length
            ? lines[lineIndex]
            : SyntaxHighlighter.Tokenize(hunk.Lines[Math.Clamp(lineIndex, 0, hunk.Lines.Count - 1)].Text, path);
    }

    internal static IReadOnlyList<CodeToken>[] Compute(DiffHunk hunk, string path)
    {
        var result = new IReadOnlyList<CodeToken>[hunk.Lines.Count];
        IStateStack? oldState = null;
        IStateStack? newState = null;

        for (var i = 0; i < hunk.Lines.Count; i++)
        {
            var line = hunk.Lines[i];
            if (line.Kind == DiffLineKind.NoNewline || line.Text.Length == 0 || line.Text.Length > MaxLineLength)
            {
                result[i] = SyntaxHighlighter.Tokenize(line.Text, path);
                continue;
            }

            switch (line.Kind)
            {
                case DiffLineKind.Remove:
                    result[i] = Run(line.Text, path, ref oldState);
                    break;
                case DiffLineKind.Add:
                    result[i] = Run(line.Text, path, ref newState);
                    break;
                default:
                    // A context line exists on both sides, so both streams advance; the new side is what we show.
                    Run(line.Text, path, ref oldState);
                    result[i] = Run(line.Text, path, ref newState);
                    break;
            }
        }

        return result;
    }

    private static IReadOnlyList<CodeToken> Run(string text, string path, ref IStateStack? state) =>
        TextMateTokenizer.Tokenize(text, path, ref state) ?? SyntaxHighlighter.TokenizeBasic(text, path);
}
