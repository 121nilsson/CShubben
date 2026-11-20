using System.Globalization;
using System.Text.RegularExpressions;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Feed;

/// <summary>
/// Consecutive events on one pull request, collapsed into one row. A group never mixes new and seen events.
/// <see cref="Items"/> are newest first and never empty.
/// </summary>
public sealed record FeedGroup(
    IReadOnlyList<FeedItem> Items,
    bool IsNew,
    string ActorLabel,
    string Action,
    FeedKind Kind,
    string Snippet)
{
    /// <summary>The id of the newest event; unique across the list.</summary>
    public string Key => Items[0].Id;

    public FeedItem Newest => Items[0];

    public DateTimeOffset At => Items[0].At;

    public string Repo => Items[0].Repo;

    public int? Number => Items[0].Number;

    public string? Title => Items.Select(i => i.Title).FirstOrDefault(t => !string.IsNullOrEmpty(t));

    public int Count => Items.Count;

    public bool IsCollapsed => Items.Count > 1;

    public bool Personal => Items.Any(i => i.Personal);

    public bool IsBot => Items.All(i => i.IsBot);

    /// <summary>For example "maria pushed 3 commits, 2 comments".</summary>
    public string Summary => ActorLabel + " " + Action;
}

public static class FeedGrouping
{
    /// <summary>Events of the same PR further apart than this are not merged into one row.</summary>
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromHours(12);

    private static readonly Regex CommitCount = new(@"(\d{1,5}) commits?$", RegexOptions.CultureInvariant);
    private static readonly Regex CommentCount = new(@"^left (\d{1,5}) comments?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Groups a newest-first list. Events join the previous group when they are on the same PR, are both new or both seen,
    /// and are at most <paramref name="maxGap"/> apart. Events without a PR number (deploys) stand alone.
    /// </summary>
    public static List<FeedGroup> Group(IReadOnlyList<FeedItem> newestFirst, DateTimeOffset? lastSeen, TimeSpan? maxGap = null)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var gap = maxGap ?? DefaultMaxGap;
        var groups = new List<(List<FeedItem> Items, bool IsNew)>();
        foreach (var item in newestFirst)
        {
            var isNew = IsNew(item, lastSeen);
            if (groups.Count > 0 && item.Number is not null)
            {
                var current = groups[groups.Count - 1];
                var last = current.Items[current.Items.Count - 1];
                if (current.IsNew == isNew
                    && last.Number == item.Number
                    && string.Equals(last.Repo, item.Repo, StringComparison.OrdinalIgnoreCase)
                    && last.At - item.At <= gap)
                {
                    current.Items.Add(item);
                    continue;
                }
            }

            groups.Add((new List<FeedItem> { item }, isNew));
        }

        return groups.Select(g => Build(g.Items, g.IsNew)).ToList();
    }

    /// <summary>An event is new when it happened after <paramref name="lastSeen"/>. With no last-seen time nothing is new.</summary>
    public static bool IsNew(FeedItem item, DateTimeOffset? lastSeen) => lastSeen is DateTimeOffset seen && item.At > seen;

    private static FeedGroup Build(List<FeedItem> items, bool isNew)
    {
        var kind = items.OrderBy(i => Significance(i.Kind)).First().Kind;
        var snippet = items.Count == 1 ? SnippetOf(items[0]) : string.Empty;
        return new FeedGroup(items, isNew, ActorLabelOf(items), ActionOf(items), kind, snippet);
    }

    /// <summary>Lower is more important; decides which icon represents a group.</summary>
    public static int Significance(FeedKind kind) => kind switch
    {
        FeedKind.CiFailed => 0,
        FeedKind.ChangesRequested => 1,
        FeedKind.Merged => 2,
        FeedKind.Approved => 3,
        FeedKind.ReviewRequested => 4,
        FeedKind.Opened => 5,
        FeedKind.CiPassed => 6,
        FeedKind.Commented => 7,
        FeedKind.Pushed => 8,
        _ => 9,
    };

    private static int PhraseOrder(FeedKind kind) => kind switch
    {
        FeedKind.Opened => 0,
        FeedKind.ReviewRequested => 1,
        FeedKind.Pushed => 2,
        FeedKind.Commented => 3,
        FeedKind.ChangesRequested => 4,
        FeedKind.Approved => 5,
        FeedKind.CiFailed => 6,
        FeedKind.CiPassed => 7,
        FeedKind.Merged => 8,
        _ => 9,
    };

    /// <summary>"maria", "maria and jonas", or "maria, jonas +2".</summary>
    public static string ActorLabelOf(IReadOnlyList<FeedItem> items)
    {
        var actors = new List<string>();
        foreach (var item in items)
        {
            if (item.Actor.Length > 0 && !actors.Contains(item.Actor, StringComparer.OrdinalIgnoreCase)) actors.Add(item.Actor);
        }

        return actors.Count switch
        {
            0 => "Someone",
            1 => actors[0],
            2 => actors[0] + " and " + actors[1],
            _ => actors[0] + ", " + actors[1] + " +" + (actors.Count - 2).ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The verb part: "pushed 3 commits, 2 comments", "approved", "requested changes".</summary>
    public static string ActionOf(IReadOnlyList<FeedItem> items)
    {
        var kinds = items.Select(i => i.Kind).Distinct().OrderBy(k => PhraseOrder(k)).ThenBy(k => (int)k).ToList();
        var parts = new List<string>();
        foreach (var kind in kinds)
        {
            var ofKind = items.Where(i => i.Kind == kind).ToList();
            var phrase = Phrase(kind, ofKind);
            if (kind == FeedKind.Commented && parts.Count == 0 && ofKind.Count > 0)
            {
                var n = CommentsIn(ofKind);
                phrase = n == 1 && items.Count == 1 ? "commented" : "left " + phrase;
            }

            parts.Add(phrase);
        }

        return string.Join(", ", parts);
    }

    private static string Phrase(FeedKind kind, List<FeedItem> ofKind) => kind switch
    {
        FeedKind.Opened => "opened",
        FeedKind.ReviewRequested => "requested review",
        FeedKind.Pushed => "pushed " + Plural(CommitsIn(ofKind), "commit"),
        FeedKind.Commented => Plural(CommentsIn(ofKind), "comment"),
        FeedKind.ChangesRequested => "requested changes",
        FeedKind.Approved => "approved",
        FeedKind.CiFailed => "CI failed",
        FeedKind.CiPassed => "CI passed",
        FeedKind.Merged => "merged",
        FeedKind.Deployed => "deployed",
        _ => "did something",
    };

    private static string Plural(int count, string noun) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");

    private static int CommitsIn(List<FeedItem> items)
    {
        var total = 0;
        foreach (var item in items)
        {
            var match = CommitCount.Match(item.Detail.Trim());
            total += match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        }

        return total;
    }

    private static int CommentsIn(List<FeedItem> items)
    {
        var total = 0;
        foreach (var item in items)
        {
            var match = CommentCount.Match(item.Detail.Trim());
            total += match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        }

        return total;
    }

    /// <summary>Text of a single comment or review, when the detail carries real text rather than a count or a repeat of the verb.</summary>
    private static string SnippetOf(FeedItem item)
    {
        if (item.Kind is not (FeedKind.Commented or FeedKind.ChangesRequested or FeedKind.Approved)) return string.Empty;
        var detail = item.Detail.Trim();
        if (detail.Length == 0) return string.Empty;
        if (CommentCount.IsMatch(detail)) return string.Empty;
        if (string.Equals(detail, "approved", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (string.Equals(detail, "requested changes", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return detail;
    }
}
