using TextMateSharp.Grammars;

namespace Cshubben.Core.Features.Review;

/// <summary>
/// Highlights one line with the VS Code (TextMate) grammars and maps the scopes to our few token kinds, so the UI keeps
/// using theme colours. Any failure (for example the native regex engine not loading) disables it and callers fall back.
/// </summary>
static class TextMateTokenizer
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IGrammar?> GrammarsByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static RegistryOptions? _options;
    private static TextMateSharp.Registry.Registry? _registry;
    private static bool _broken;

    public static bool Supports(string path)
    {
        lock (Gate) return GrammarFor(path) is not null;
    }

    /// <summary>Tokens for the line, or null when there is no grammar or the engine is unavailable.</summary>
    public static IReadOnlyList<CodeToken>? Tokenize(string text, string path)
    {
        IStateStack? state = null;
        return Tokenize(text, path, ref state);
    }

    /// <summary>Like the stateless overload, but starts from <paramref name="state"/> and leaves the state after the line in it.</summary>
    public static IReadOnlyList<CodeToken>? Tokenize(string text, string path, ref IStateStack? state)
    {
        lock (Gate)
        {
            try
            {
                var grammar = GrammarFor(path);
                if (grammar is null) return null;

                var result = grammar.TokenizeLine(text, state, TimeSpan.FromSeconds(2));
                state = result.RuleStack;
                var tokens = new List<CodeToken>();
                var pos = 0;
                foreach (var token in result.Tokens)
                {
                    var start = Math.Clamp(token.StartIndex, pos, text.Length);
                    var end = Math.Clamp(token.EndIndex, start, text.Length);
                    if (start > pos) Add(tokens, text.Substring(pos, start - pos), TokenKind.Plain);
                    if (end > start) Add(tokens, text.Substring(start, end - start), KindOf(token.Scopes));
                    pos = end;
                }

                if (pos < text.Length) Add(tokens, text.Substring(pos), TokenKind.Plain);
                return tokens;
            }
            catch (Exception)
            {
                _broken = true;
                return null;
            }
        }
    }

    internal static TokenKind KindOf(IReadOnlyList<string> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var scope = scopes[i];
            if (scope.StartsWith("meta.template.expression", StringComparison.Ordinal)
                || scope.StartsWith("meta.interpolation", StringComparison.Ordinal)
                || scope.StartsWith("meta.embedded", StringComparison.Ordinal)) return TokenKind.Plain;
            if (scope.StartsWith("comment", StringComparison.Ordinal)) return TokenKind.Comment;
            if (scope.StartsWith("string", StringComparison.Ordinal)) return TokenKind.String;
            if (scope.StartsWith("constant.numeric", StringComparison.Ordinal)) return TokenKind.Number;
            if (scope.StartsWith("keyword", StringComparison.Ordinal) || scope.StartsWith("storage", StringComparison.Ordinal)
                || scope.StartsWith("constant.language", StringComparison.Ordinal)
                || scope.StartsWith("entity.name.tag", StringComparison.Ordinal)
                || scope.StartsWith("support.class.component", StringComparison.Ordinal)) return TokenKind.Keyword;
        }

        return TokenKind.Plain;
    }

    private static IGrammar? GrammarFor(string path)
    {
        if (_broken) return null;
        var dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1) return null;
        var ext = path.Substring(dot);
        if (GrammarsByExtension.TryGetValue(ext, out var cached)) return cached;

        IGrammar? grammar = null;
        try
        {
            _options ??= new RegistryOptions(ThemeName.DarkPlus);
            var language = _options.GetLanguageByExtension(ext);
            if (language is not null)
            {
                _registry ??= new TextMateSharp.Registry.Registry(_options);
                grammar = _registry.LoadGrammar(_options.GetScopeByLanguageId(language.Id));
            }
        }
        catch (Exception)
        {
            _broken = true;
        }

        GrammarsByExtension[ext] = grammar;
        return grammar;
    }

    private static void Add(List<CodeToken> tokens, string text, TokenKind kind)
    {
        if (tokens.Count > 0 && tokens[^1].Kind == kind)
            tokens[^1] = new CodeToken(tokens[^1].Text + text, kind);
        else
            tokens.Add(new CodeToken(text, kind));
    }
}
