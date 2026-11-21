using System.Text;

namespace Cshubben.Core.Features.Review;

public enum TokenKind { Plain, Keyword, String, Comment, Number }

public readonly record struct CodeToken(string Text, TokenKind Kind);

/// <summary>
/// A small, stateless, single-line highlighter for diff lines. It knows three families of languages
/// (C-like, hash-comment and JSON) and leaves everything else plain. It is a reading aid, not a parser,
/// so multi-line constructs are only approximated.
/// </summary>
public static class SyntaxHighlighter
{
    private enum Family { None, CLike, Hash, Json }

    private const int MaxLineLength = 400;

    private static readonly HashSet<string> CLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "ts", "tsx", "js", "jsx", "mjs", "cjs", "java", "go", "rs", "c", "h", "cpp", "hpp", "cc", "kt", "swift", "scala", "dart",
    };

    private static readonly HashSet<string> HashExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "py", "rb", "sh", "bash", "zsh", "yml", "yaml", "toml", "ps1", "r",
    };

    private static readonly HashSet<string> JsonExtensions = new(StringComparer.OrdinalIgnoreCase) { "json", "jsonc" };

    private static readonly HashSet<string> CLikeKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "bool", "break", "case", "catch", "char", "class", "const", "continue", "default",
        "delegate", "do", "double", "else", "enum", "export", "extends", "false", "final", "finally", "float", "fn", "for",
        "foreach", "func", "function", "if", "impl", "import", "in", "interface", "int", "internal", "is", "let", "long", "match",
        "mod", "mut", "namespace", "new", "nil", "null", "override", "package", "private", "protected", "pub", "public",
        "readonly", "record", "ref", "return", "sealed", "self", "static", "string", "struct", "super", "switch", "this",
        "throw", "trait", "true", "try", "type", "typeof", "use", "using", "var", "virtual", "void", "where", "while", "yield",
        // C#
        "base", "byte", "checked", "decimal", "dynamic", "event", "explicit", "extern", "fixed", "global", "goto", "implicit",
        "init", "lock", "nameof", "object", "operator", "out", "params", "partial", "required", "sbyte", "short", "sizeof",
        "stackalloc", "uint", "ulong", "unchecked", "unsafe", "ushort", "volatile", "when", "with",
        // TypeScript / JavaScript
        "declare", "delete", "implements", "instanceof", "keyof", "satisfies", "undefined",
    };

    private static readonly HashSet<string> HashKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "do", "done", "elif", "else",
        "end", "esac", "except", "false", "False", "fi", "finally", "for", "from", "function", "global", "if", "import", "in",
        "is", "lambda", "None", "nonlocal", "not", "null", "or", "pass", "raise", "return", "then", "true", "True", "try",
        "while", "with", "yield",
    };

    private static readonly HashSet<string> JsonKeywords = new(StringComparer.Ordinal) { "true", "false", "null" };

    public static IReadOnlyList<CodeToken> Tokenize(string text, string path)
    {
        var family = FamilyOf(path);
        if (family == Family.None || text.Length == 0 || text.Length > MaxLineLength)
            return new[] { new CodeToken(text, TokenKind.Plain) };

        var ext = path.Substring(path.LastIndexOf('.') + 1).ToLowerInvariant();
        var isCSharp = ext == "cs";
        var isJsx = ext is "jsx" or "tsx" or "js";
        var keywords = family == Family.CLike ? CLikeKeywords : family == Family.Hash ? HashKeywords : JsonKeywords;
        var tokens = new List<CodeToken>();
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length == 0) return;
            Add(tokens, plain.ToString(), TokenKind.Plain);
            plain.Clear();
        }

        if (isCSharp && text.TrimStart().StartsWith('#'))
            return new[] { new CodeToken(text, TokenKind.Keyword) };

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (isJsx && c == '<' && i + 1 < text.Length && (i == 0 || !IsIdentifierChar(text[i - 1]))
                && (char.IsLetter(text[i + 1]) || (text[i + 1] == '/' && i + 2 < text.Length && char.IsLetter(text[i + 2]))))
            {
                var j = i + (text[i + 1] == '/' ? 2 : 1);
                while (j < text.Length && (IsIdentifierChar(text[j]) || text[j] == '.' || text[j] == '-')) j++;
                plain.Append(text, i, text[i + 1] == '/' ? 2 : 1);
                Flush();
                Add(tokens, text.Substring(i + (text[i + 1] == '/' ? 2 : 1), j - i - (text[i + 1] == '/' ? 2 : 1)), TokenKind.Keyword);
                i = j;
                continue;
            }

            if (isCSharp && c == '"' && string.CompareOrdinal(text, i, "\"\"\"", 0, 3) == 0)
            {
                Flush();
                var end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 3;
                Add(tokens, text.Substring(i, stop - i), TokenKind.String);
                i = stop;
                continue;
            }

            if (isCSharp && c == '"' && i > 0 && (text[i - 1] == '@' || (i > 1 && text[i - 1] == '$' && text[i - 2] == '@') || (i > 1 && text[i - 1] == '@' && text[i - 2] == '$')))
            {
                var j = i + 1;
                while (j < text.Length)
                {
                    if (text[j] == '"') { if (j + 1 < text.Length && text[j + 1] == '"') { j += 2; continue; } break; }
                    j++;
                }

                if (j < text.Length)
                {
                    Flush();
                    Add(tokens, text.Substring(i, j - i + 1), TokenKind.String);
                    i = j + 1;
                    continue;
                }
            }

            if (family == Family.CLike && c == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                Flush();
                var end = text[i + 1] == '/' ? -1 : text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                Add(tokens, text.Substring(i, stop - i), TokenKind.Comment);
                i = stop;
                continue;
            }

            if (family == Family.Hash && c == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
            {
                Flush();
                Add(tokens, text.Substring(i), TokenKind.Comment);
                break;
            }

            if (c == '"' || (c == '`' && family == Family.CLike) || (c == '\'' && family != Family.Json))
            {
                var close = FindClosingQuote(text, i);
                if (close > 0)
                {
                    Flush();
                    Add(tokens, text.Substring(i, close - i + 1), TokenKind.String);
                    i = close + 1;
                    continue;
                }
            }

            if (char.IsDigit(c) && (i == 0 || !IsIdentifierChar(text[i - 1])))
            {
                Flush();
                var j = i + 1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '.' || text[j] == '_')) j++;
                Add(tokens, text.Substring(i, j - i), TokenKind.Number);
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i + 1;
                while (j < text.Length && IsIdentifierChar(text[j])) j++;
                var word = text.Substring(i, j - i);
                if (keywords.Contains(word))
                {
                    Flush();
                    Add(tokens, word, TokenKind.Keyword);
                }
                else
                {
                    plain.Append(word);
                }

                i = j;
                continue;
            }

            plain.Append(c);
            i++;
        }

        Flush();
        return tokens;
    }

    public static bool IsHighlighted(string path) => FamilyOf(path) != Family.None;

    private static void Add(List<CodeToken> tokens, string text, TokenKind kind)
    {
        if (tokens.Count > 0 && tokens[^1].Kind == kind)
            tokens[^1] = new CodeToken(tokens[^1].Text + text, kind);
        else
            tokens.Add(new CodeToken(text, kind));
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Index of the quote that closes the string opened at <paramref name="open"/>, or -1 when the line ends first.</summary>
    private static int FindClosingQuote(string text, int open)
    {
        var quote = text[open];
        for (var i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == quote) return i;
        }

        return -1;
    }

    private static Family FamilyOf(string path)
    {
        var dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1) return Family.None;
        var ext = path.Substring(dot + 1);
        if (CLikeExtensions.Contains(ext)) return Family.CLike;
        if (HashExtensions.Contains(ext)) return Family.Hash;
        if (JsonExtensions.Contains(ext)) return Family.Json;
        return Family.None;
    }
}
