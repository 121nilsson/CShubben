using System.Globalization;

namespace Cshubben.Core.Features.Pipeline;

public enum ConversationKind { Comment, Review, Thread, Commit, Event }

/// <summary>The state a review leaves behind. <see cref="None"/> for everything that is not a review.</summary>
public enum ReviewStateKind { None, Approved, ChangesRequested, Commented, Dismissed }

/// <summary>A reply inside a review thread.</summary>
public sealed record ThreadReply(string Author, DateTimeOffset At, string Body);

/// <summary>
/// One entry of the unified pull request timeline: an issue comment, a review, an inline thread, a commit
/// (or a run of commits by the same author) or an event such as "marked ready for review".
/// </summary>
public sealed record ConversationItem(
    string Id,
    ConversationKind Kind,
    string Author,
    bool IsBot,
    DateTimeOffset At,
    string Body,
    string Url)
{
    /// <summary>Review state, for reviews only.</summary>
    public ReviewStateKind Review { get; init; }

    /// <summary>For threads: true when resolved. Other kinds are always "resolved" (nothing to do).</summary>
    public bool Resolved { get; init; } = true;

    public bool Outdated { get; init; }

    public string? Path { get; init; }

    public int? Line { get; init; }

    public IReadOnlyList<ThreadReply> Replies { get; init; } = Array.Empty<ThreadReply>();

    /// <summary>Commit id, for commits.</summary>
    public string? Sha { get; init; }

    /// <summary>How many commits this entry stands for (more than one after grouping).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Commit headline or event text such as "added the label bug".</summary>
    public string Title { get; init; } = string.Empty;

    public bool IsUnresolvedThread => Kind == ConversationKind.Thread && !Resolved;

    /// <summary>Entries people wrote, as opposed to commits and events.</summary>
    public bool IsDiscussion => Kind is ConversationKind.Comment or ConversationKind.Review or ConversationKind.Thread;
}

/// <summary>The whole timeline of a pull request, oldest first.</summary>
public sealed record PrConversation(IReadOnlyList<ConversationItem> Items, string HeadSha)
{
    public static PrConversation Empty { get; } = new(Array.Empty<ConversationItem>(), string.Empty);

    public int UnresolvedThreads => Items.Count(i => i.IsUnresolvedThread);

    public int CommentCount => Items.Count(i => i.Kind == ConversationKind.Comment && !i.IsBot);

    /// <summary>Logins whose latest decisive review is an approval (not dismissed, not changed since).</summary>
    public IReadOnlyList<string> Approvers => Stance(ReviewStateKind.Approved);

    /// <summary>Logins whose latest decisive review asks for changes.</summary>
    public IReadOnlyList<string> ChangesRequestedBy => Stance(ReviewStateKind.ChangesRequested);

    /// <summary>The newest item, or null when empty.</summary>
    public ConversationItem? Latest => Items.Count == 0 ? null : Items[Items.Count - 1];

    /// <summary>One short line, for example "3 comments · 1 unresolved thread · approved by priya".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            var comments = Items.Count(i => i.IsDiscussion && !i.IsBot);
            parts.Add(PipelineFormat.Count(comments, "comment"));
            var open = UnresolvedThreads;
            if (open > 0) parts.Add(PipelineFormat.Count(open, "unresolved thread"));
            var approvers = Approvers;
            if (approvers.Count > 0) parts.Add("approved by " + string.Join(", ", approvers));
            var changes = ChangesRequestedBy;
            if (changes.Count > 0) parts.Add("changes requested by " + string.Join(", ", changes));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Only what people wrote, or everything including commits and events.</summary>
    public IReadOnlyList<ConversationItem> Visible(bool includeActivity) =>
        includeActivity ? Items : Items.Where(i => i.IsDiscussion).ToList();

    private IReadOnlyList<string> Stance(ReviewStateKind wanted)
    {
        var latest = new Dictionary<string, ConversationItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (item.Kind != ConversationKind.Review) continue;
            if (item.Review is ReviewStateKind.None or ReviewStateKind.Commented) continue;
            if (!latest.TryGetValue(item.Author, out var known) || item.At >= known.At) latest[item.Author] = item;
        }

        return latest.Values
            .Where(i => i.Review == wanted)
            .OrderBy(i => i.At)
            .Select(i => i.Author)
            .ToList();
    }
}

/// <summary>Builds the ordered, compact timeline.</summary>
public static class ConversationBuilder
{
    /// <summary>Longest body kept for display; the rest is cut with an ellipsis.</summary>
    public const int MaxBodyLength = 3000;

    /// <summary>Orders items oldest first and merges consecutive commits by the same author into one entry.</summary>
    public static PrConversation Build(IEnumerable<ConversationItem> items, string headSha)
    {
        var ordered = items
            .OrderBy(i => i.At)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
        return new PrConversation(GroupCommits(ordered), headSha);
    }

    /// <summary>Consecutive commits by one author become "pushed N commits" with the newest headline and sha.</summary>
    public static IReadOnlyList<ConversationItem> GroupCommits(IReadOnlyList<ConversationItem> ordered)
    {
        var result = new List<ConversationItem>();
        foreach (var item in ordered)
        {
            if (item.Kind == ConversationKind.Commit && result.Count > 0)
            {
                var last = result[result.Count - 1];
                if (last.Kind == ConversationKind.Commit
                    && string.Equals(last.Author, item.Author, StringComparison.OrdinalIgnoreCase))
                {
                    result[result.Count - 1] = item with { Count = last.Count + item.Count };
                    continue;
                }
            }
            result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Cleans untrusted text: control characters other than line breaks are dropped, line breaks are normalised
    /// and the length is capped.
    /// </summary>
    public static string CleanBody(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new System.Text.StringBuilder(Math.Min(normalized.Length, MaxBodyLength + 1));
        foreach (var c in normalized)
        {
            if (c == '\n' || c == '\t') sb.Append(c);
            else if (!char.IsControl(c)) sb.Append(c);
            if (sb.Length > MaxBodyLength) break;
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length > MaxBodyLength ? cleaned.Substring(0, MaxBodyLength).TrimEnd() + "…" : cleaned;
    }

    /// <summary>"3 comments" style counts for headers.</summary>
    public static string Describe(ConversationItem item) => item.Kind switch
    {
        ConversationKind.Commit when item.Count > 1 => "pushed " + item.Count.ToString(CultureInfo.InvariantCulture) + " commits",
        ConversationKind.Commit => "pushed a commit",
        ConversationKind.Comment => "commented",
        ConversationKind.Thread => item.Path is null ? "started a thread" : "commented on " + item.Path,
        ConversationKind.Review => item.Review switch
        {
            ReviewStateKind.Approved => "approved",
            ReviewStateKind.ChangesRequested => "requested changes",
            ReviewStateKind.Dismissed => "had a review dismissed",
            _ => "reviewed",
        },
        _ => item.Title,
    };
}
