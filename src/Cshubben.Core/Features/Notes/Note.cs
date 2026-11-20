using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Notes;

public enum NoteLinkKind { Pr, Issue, Url, Repo }

/// <summary>A reference from a note to a PR, a Linear issue, a web page or a repository.</summary>
/// <param name="Kind">What the target is.</param>
/// <param name="Target">For example "acme/api#412", "ENG-123", "https://example.com" or "acme/api".</param>
/// <param name="Label">Human readable text; may be empty.</param>
public sealed record NoteLink(NoteLinkKind Kind, string Target, string Label)
{
    /// <summary>Guesses the kind from the shape of the target.</summary>
    public static NoteLink Parse(string target, string label = "")
    {
        var t = target.Trim();
        return new NoteLink(GuessKind(t), t, label);
    }

    private static NoteLinkKind GuessKind(string t)
    {
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return NoteLinkKind.Url;

        var hash = t.IndexOf('#');
        if (hash > 0 && hash < t.Length - 1 && t.IndexOf('/') > 0 && t.IndexOf('/') < hash && AllDigits(t, hash + 1))
            return NoteLinkKind.Pr;

        var dash = t.LastIndexOf('-');
        if (dash > 0 && dash < t.Length - 1 && AllDigits(t, dash + 1) && IsTeamKey(t, dash))
            return NoteLinkKind.Issue;

        var slash = t.IndexOf('/');
        if (slash > 0 && slash < t.Length - 1 && t.IndexOf('/', slash + 1) < 0 && t.IndexOf(' ') < 0)
            return NoteLinkKind.Repo;

        return NoteLinkKind.Url;
    }

    private static bool AllDigits(string s, int from)
    {
        for (var i = from; i < s.Length; i++)
            if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    private static bool IsTeamKey(string s, int length)
    {
        for (var i = 0; i < length; i++)
            if (!(s[i] >= 'A' && s[i] <= 'Z') && !(i > 0 && s[i] >= '0' && s[i] <= '9')) return false;
        return true;
    }
}

/// <summary>A markdown note with tags, links and attached files.</summary>
public sealed record Note(
    string Id,
    string Title,
    string Body,
    IReadOnlyList<string> Tags,
    IReadOnlyList<NoteLink> Links,
    IReadOnlyList<string> Attachments,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

    public static Note Create(
        string title = "",
        string body = "",
        IEnumerable<string>? tags = null,
        IEnumerable<NoteLink>? links = null,
        bool pinned = false,
        DateTimeOffset? now = null,
        string? id = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        return new Note(
            id ?? NewId(),
            title,
            body,
            tags is null ? Array.Empty<string>() : tags.ToList(),
            links is null ? Array.Empty<NoteLink>() : links.ToList(),
            Array.Empty<string>(),
            pinned,
            at,
            at);
    }

    /// <summary>A fresh note about a pull request, linked to it.</summary>
    public static Note FromPullRequest(PullRequest pr, DateTimeOffset? now = null)
    {
        var target = $"{pr.Repo}#{pr.Number}";
        var body = $"**{pr.Title}**\n\n";
        return Create(
            $"Notes on {pr.Repo} #{pr.Number}",
            body,
            new[] { "pr" },
            new[] { new NoteLink(NoteLinkKind.Pr, target, pr.Title) },
            false,
            now);
    }

    /// <summary>A note holding saved text, for example an answer from Claude. Source may be a PR, an issue or a URL.</summary>
    public static Note FromText(string title, string body, string? source = null, DateTimeOffset? now = null)
    {
        var links = string.IsNullOrWhiteSpace(source)
            ? Array.Empty<NoteLink>()
            : new[] { NoteLink.Parse(source) };
        return Create(title, body, new[] { "claude" }, links, false, now);
    }
}
