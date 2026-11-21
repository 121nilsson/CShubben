using System.Globalization;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Notes;

public enum NoteReferenceKind { PullRequest, Issue }

/// <summary>
/// A pull request or Linear issue mentioned in running text.
/// </summary>
/// <param name="Kind">Pull request or Linear issue.</param>
/// <param name="Repo">"owner/repo" for pull requests, empty for issues.</param>
/// <param name="Number">PR number; 0 for issues.</param>
/// <param name="Identifier">Linear identifier such as "API-231"; empty for pull requests.</param>
/// <param name="Start">Index of the first character of the reference in the text.</param>
/// <param name="Length">Number of characters the reference covers (for URLs, the whole URL).</param>
public sealed record NoteReference(NoteReferenceKind Kind, string Repo, int Number, string Identifier, int Start, int Length)
{
    /// <summary>"acme/api#412" or "API-231".</summary>
    public string Display => Kind == NoteReferenceKind.PullRequest ? Repo + "#" + Number.ToString(CultureInfo.InvariantCulture) : Identifier;

    /// <summary>The GitHub or Linear web address for the reference.</summary>
    public string WebUrl => Kind == NoteReferenceKind.PullRequest
        ? "https://github.com/" + Repo + "/pull/" + Number.ToString(CultureInfo.InvariantCulture)
        : "https://linear.app/issue/" + Identifier;
}

/// <summary>
/// Finds pull request and Linear issue references in note text. Pure and tolerant: unknown shapes are left alone.
/// Matches inside code (fenced blocks and inline code spans) are ignored.
/// </summary>
public static class NoteReferences
{
    private const int MaxNumberDigits = 7;

    // Standards and words that look like Linear identifiers but are not.
    private static readonly HashSet<string> NotTeamKeys = new(StringComparer.Ordinal)
    {
        "UTF", "ISO", "RFC", "CVE", "AES", "TLS", "SSL", "MD", "RSA", "WCAG", "PEP", "HTTP", "UTC", "IPV", "ASCII", "GPT",
        "FIX", "BUG", "PR", "WIP", "TASK", "FEAT", "CHORE", "ISSUE", "PATCH", "TEST", "DOCS", "GH", "HOTFIX", "RELEASE", "VERSION",
    };

    /// <summary>
    /// All references in <paramref name="text"/>, in text order, without overlaps. A bare "#412" counts only when
    /// <paramref name="contextRepos"/> holds exactly one distinct repository (case-insensitive); otherwise it is ambiguous and skipped.
    /// </summary>
    public static IReadOnlyList<NoteReference> Find(string? text, IReadOnlyCollection<string>? contextRepos = null)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<NoteReference>();
        var single = SingleRepo(contextRepos);
        var code = CodeMask(text);
        var result = new List<NoteReference>();
        var i = 0;
        while (i < text.Length)
        {
            if (code[i]) { i++; continue; }
            var r = TryUrl(text, i) ?? TryRepoPr(text, i) ?? TryBarePr(text, i, single) ?? TryIssue(text, i);
            if (r is null || AnyCode(code, r.Start, r.Length)) { i++; continue; }
            result.Add(r);
            i = r.Start + r.Length;
        }
        return result;
    }

    /// <summary>References in a note: its body, with the repositories of its explicit PR links and explicit PR mentions as context.</summary>
    public static IReadOnlyList<NoteReference> ForNote(Note note)
    {
        var repos = ContextRepos(note);
        return Find(note.Body, repos);
    }

    /// <summary>The references in a note's body, each target once (first occurrence), for a "mentioned here" list.</summary>
    public static IReadOnlyList<NoteReference> DistinctForNote(Note note)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ForNote(note).Where(r => seen.Add(r.Display)).ToList();
    }

    /// <summary>Every pull request a note points at: PR links in its header and PR references in its body. Distinct, in order.</summary>
    public static IReadOnlyList<(string Repo, int Number)> PullRequestsOf(Note note)
    {
        var seen = new HashSet<(string, int)>();
        var list = new List<(string Repo, int Number)>();
        void Add(string repo, int number)
        {
            if (seen.Add((repo.ToLowerInvariant(), number))) list.Add((repo, number));
        }

        foreach (var link in note.Links)
            if (link.Kind == NoteLinkKind.Pr && TryParsePrTarget(link.Target, out var repo, out var number))
                Add(repo, number);
        foreach (var r in ForNote(note))
            if (r.Kind == NoteReferenceKind.PullRequest) Add(r.Repo, r.Number);
        return list;
    }

    /// <summary>Notes that mention the pull request (by link or in the text), pinned first, then newest.</summary>
    public static IReadOnlyList<Note> NotesMentioningPr(IEnumerable<Note> notes, string repo, int number) =>
        notes
            .Where(n => PullRequestsOf(n).Any(p => p.Number == number && string.Equals(p.Repo, repo, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>Notes that mention the Linear issue (by link or in the text), pinned first, then newest.</summary>
    public static IReadOnlyList<Note> NotesMentioningIssue(IEnumerable<Note> notes, string identifier) =>
        notes
            .Where(n => n.Links.Any(l => l.Kind == NoteLinkKind.Issue && string.Equals(l.Target.Trim(), identifier, StringComparison.OrdinalIgnoreCase))
                        || ForNote(n).Any(r => r.Kind == NoteReferenceKind.Issue && string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>The pull request in <paramref name="prs"/> that a reference points at, or null when it is not in the list.</summary>
    public static PullRequest? FindPullRequest(IEnumerable<PullRequest> prs, string repo, int number) =>
        prs.FirstOrDefault(p => p.Number == number && string.Equals(p.Repo, repo, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The web address a note link opens: GitHub for PRs and repositories, Linear for issues, the target itself for http(s) URLs.
    /// Null when there is nothing safe to open (for example a "javascript:" or "file:" target).
    /// </summary>
    public static string? LinkUrl(NoteLink link)
    {
        var target = link.Target.Trim();
        switch (link.Kind)
        {
            case NoteLinkKind.Pr:
                return TryParsePrTarget(target, out var repo, out var number)
                    ? "https://github.com/" + repo + "/pull/" + number.ToString(CultureInfo.InvariantCulture)
                    : null;
            case NoteLinkKind.Issue:
                return IsIssueIdentifier(target) ? "https://linear.app/issue/" + target.ToUpperInvariant() : null;
            case NoteLinkKind.Repo:
                return IsRepoName(target) ? "https://github.com/" + target : null;
            default:
                return Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? uri.AbsoluteUri
                    : null;
        }
    }

    /// <summary>Splits "acme/api#412" into repository and number.</summary>
    public static bool TryParsePrTarget(string? target, out string repo, out int number)
    {
        repo = "";
        number = 0;
        if (string.IsNullOrWhiteSpace(target)) return false;
        var t = target.Trim();
        var hash = t.LastIndexOf('#');
        if (hash <= 0 || hash == t.Length - 1) return false;
        var name = t.Substring(0, hash);
        if (!IsRepoName(name)) return false;
        if (!TryNumber(t, hash + 1, out var parsed, out var end) || end != t.Length) return false;
        number = parsed;
        repo = name;
        return true;
    }

    // ------------------------------------------------------------------ matchers

    private static NoteReference? TryUrl(string text, int i)
    {
        var prefixLength = StartsWithAt(text, i, "https://github.com/") ? 19 : StartsWithAt(text, i, "https://linear.app/") ? 19 : 0;
        if (prefixLength == 0 || !IsBoundaryBefore(text, i)) return null;
        var end = i;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ')' && text[end] != '>' && text[end] != ']' && text[end] != '"') end++;
        var url = text.Substring(i, end - i).TrimEnd('.', ',', ';', ':', '!', '?');
        var parts = url.Substring(prefixLength).Split('/');

        if (prefixLength == 19 && url.StartsWith("https://github.com/", StringComparison.Ordinal))
        {
            // owner/repo/pull/123[/files][?x][#y]
            if (parts.Length < 4 || parts[2] != "pull") return null;
            var number = Digits(parts[3]);
            if (number is null || !IsOwnerOrRepo(parts[0]) || !IsOwnerOrRepo(parts[1])) return null;
            return new NoteReference(NoteReferenceKind.PullRequest, parts[0] + "/" + parts[1], number.Value, "", i, url.Length);
        }

        // linear.app/<workspace>/issue/ENG-123[/slug]
        if (parts.Length < 3 || parts[1] != "issue") return null;
        var id = parts[2];
        var dash = id.IndexOf('-');
        if (dash <= 0 || !IsIssueIdentifier(id)) return null;
        return new NoteReference(NoteReferenceKind.Issue, "", 0, id.ToUpperInvariant(), i, url.Length);
    }

    private static NoteReference? TryRepoPr(string text, int i)
    {
        if (!IsRepoChar(text[i]) || !IsBoundaryBefore(text, i)) return null;
        var slash = i;
        while (slash < text.Length && IsRepoChar(text[slash])) slash++;
        if (slash >= text.Length || text[slash] != '/' || slash == i) return null;
        var nameEnd = slash + 1;
        while (nameEnd < text.Length && IsRepoChar(text[nameEnd])) nameEnd++;
        if (nameEnd == slash + 1 || nameEnd >= text.Length || text[nameEnd] != '#') return null;
        if (!TryNumber(text, nameEnd + 1, out var number, out var end)) return null;
        if (end < text.Length && IsWordChar(text[end])) return null;
        var repo = text.Substring(i, nameEnd - i);
        if (!IsRepoName(repo)) return null;
        return new NoteReference(NoteReferenceKind.PullRequest, repo, number, "", i, end - i);
    }

    private static NoteReference? TryBarePr(string text, int i, string? repo)
    {
        if (repo is null || text[i] != '#') return null;
        if (i > 0 && !char.IsWhiteSpace(text[i - 1]) && text[i - 1] != '(' && text[i - 1] != '[' && text[i - 1] != ',') return null;
        if (!TryNumber(text, i + 1, out var number, out var end)) return null;
        if (end < text.Length && IsWordChar(text[end])) return null;
        return new NoteReference(NoteReferenceKind.PullRequest, repo, number, "", i, end - i);
    }

    private static NoteReference? TryIssue(string text, int i)
    {
        if (!(text[i] >= 'A' && text[i] <= 'Z') || !IsBoundaryBefore(text, i)) return null;
        var p = i;
        while (p < text.Length && ((text[p] >= 'A' && text[p] <= 'Z') || (p > i && text[p] >= '0' && text[p] <= '9'))) p++;
        if (p >= text.Length || text[p] != '-') return null;
        var key = text.Substring(i, p - i);
        if (key.Length < 2 || key.Length > 10 || NotTeamKeys.Contains(key)) return null;
        if (!TryNumber(text, p + 1, out var number, out var end) || end - (p + 1) > 6) return null;
        if (end < text.Length && IsWordChar(text[end])) return null;
        if (key == "SHA" || key == "HS" || key == "RS" || key == "ES" || key == "PS")
            if (number is 1 or 2 or 3 or 224 or 256 or 384 or 512) return null;
        return new NoteReference(NoteReferenceKind.Issue, "", 0, key + "-" + text.Substring(p + 1, end - p - 1), i, end - i);
    }

    // ------------------------------------------------------------------ helpers

    private static IReadOnlyCollection<string> ContextRepos(Note note)
    {
        var repos = new List<string>();
        foreach (var link in note.Links)
            if (link.Kind == NoteLinkKind.Pr && TryParsePrTarget(link.Target, out var repo, out _)) repos.Add(repo);
        foreach (var r in Find(note.Body, null))
            if (r.Kind == NoteReferenceKind.PullRequest) repos.Add(r.Repo);
        return repos;
    }

    private static string? SingleRepo(IReadOnlyCollection<string>? repos)
    {
        if (repos is null) return null;
        string? only = null;
        foreach (var r in repos)
        {
            if (only is null) only = r;
            else if (!string.Equals(only, r, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return only;
    }

    /// <summary>Marks characters inside fenced code blocks and inline code spans.</summary>
    private static bool[] CodeMask(string text)
    {
        var mask = new bool[text.Length];
        var pos = 0;
        var inFence = false;
        while (pos < text.Length)
        {
            var nl = text.IndexOf('\n', pos);
            var lineEnd = nl < 0 ? text.Length : nl;
            var trimmed = text.AsSpan(pos, lineEnd - pos).TrimStart();
            var fence = trimmed.StartsWith("```".AsSpan()) || trimmed.StartsWith("~~~".AsSpan());
            if (fence || inFence)
            {
                for (var k = pos; k < lineEnd; k++) mask[k] = true;
                if (fence) inFence = !inFence;
            }
            else
            {
                var open = -1;
                for (var k = pos; k < lineEnd; k++)
                {
                    if (text[k] != '`') { if (open >= 0) mask[k] = true; continue; }
                    mask[k] = true;
                    open = open < 0 ? k : -1;
                }
                if (open >= 0)
                    for (var k = open; k < lineEnd; k++) mask[k] = false; // unmatched backtick: no code span
            }
            pos = lineEnd + 1;
        }
        return mask;
    }

    private static bool AnyCode(bool[] mask, int start, int length)
    {
        for (var k = start; k < start + length && k < mask.Length; k++)
            if (mask[k]) return true;
        return false;
    }

    private static bool StartsWithAt(string text, int i, string prefix) =>
        string.CompareOrdinal(text, i, prefix, 0, prefix.Length) == 0;

    private static bool IsBoundaryBefore(string text, int i) => i == 0 || !(IsWordChar(text[i - 1]) || text[i - 1] == '/' || text[i - 1] == '.' || text[i - 1] == '@');

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static bool IsRepoChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.';

    private static bool IsOwnerOrRepo(string s) => s.Length > 0 && s.Length <= 100 && s.All(IsRepoChar) && s != "." && s != "..";

    private static bool IsRepoName(string s)
    {
        var parts = s.Split('/');
        return parts.Length == 2 && IsOwnerOrRepo(parts[0]) && IsOwnerOrRepo(parts[1]);
    }

    private static bool IsIssueIdentifier(string id)
    {
        var dash = id.IndexOf('-');
        if (dash < 2 || dash > 10) return false;
        var key = id.Substring(0, dash).ToUpperInvariant();
        if (NotTeamKeys.Contains(key) || !key.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(key[0])) return false;
        var n = Digits(id.Substring(dash + 1));
        return n is not null && id.Length - dash - 1 <= 6;
    }

    private static int? Digits(string s)
    {
        if (s.Length == 0 || s.Length > MaxNumberDigits || !s.All(char.IsAsciiDigit)) return null;
        var n = int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
        return n > 0 ? n : null;
    }

    private static bool TryNumber(string text, int from, out int number, out int end)
    {
        number = 0;
        end = from;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        var len = end - from;
        if (len < 1 || len > MaxNumberDigits) return false;
        number = int.Parse(text.AsSpan(from, len), NumberStyles.None, CultureInfo.InvariantCulture);
        return number > 0;
    }
}
