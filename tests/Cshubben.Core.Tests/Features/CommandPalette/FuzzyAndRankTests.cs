using Cshubben.Core.Features.CommandPalette;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.CommandPalette;

public class FuzzyMatcherTests
{
    [Fact]
    public void Empty_query_matches_everything_with_score_zero()
    {
        var m = FuzzyMatcher.Match("", "anything");
        Assert.NotNull(m);
        Assert.Equal(0, m!.Score);
        Assert.Empty(m.Indices);
    }

    [Fact]
    public void Characters_must_appear_in_order()
    {
        Assert.NotNull(FuzzyMatcher.Match("rvw", "Review queue"));
        Assert.Null(FuzzyMatcher.Match("wvr", "Review queue"));
        Assert.Null(FuzzyMatcher.Match("xyz", "Review queue"));
        Assert.Null(FuzzyMatcher.Match("abc", null));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.NotNull(FuzzyMatcher.Match("REVIEW", "review queue"));
        Assert.NotNull(FuzzyMatcher.Match("review", "REVIEW QUEUE"));
    }

    [Fact]
    public void Query_longer_than_the_text_never_matches()
    {
        Assert.Null(FuzzyMatcher.Match("settingsx", "settings"));
    }

    [Fact]
    public void Consecutive_run_beats_scattered_letters()
    {
        var run = FuzzyMatcher.Match("set", "Settings")!;
        var scattered = FuzzyMatcher.Match("set", "Sync everything today")!;
        Assert.True(run.Score > scattered.Score);
    }

    [Fact]
    public void Word_start_beats_middle_of_word()
    {
        var start = FuzzyMatcher.Match("q", "Review queue")!;
        var middle = FuzzyMatcher.Match("u", "Review queue")!;
        Assert.True(start.Score > middle.Score);
    }

    [Fact]
    public void Indices_point_at_the_best_alignment()
    {
        var m = FuzzyMatcher.Match("rq", "Review queue")!;
        Assert.Equal(new[] { 0, 7 }, m.Indices);
    }

    [Fact]
    public void Exact_text_scores_above_a_longer_text_with_the_same_prefix()
    {
        var exact = FuzzyMatcher.Match("feed", "Feed")!;
        var longer = FuzzyMatcher.Match("feed", "Feedback form")!;
        Assert.True(exact.Score > longer.Score);
    }

    [Fact]
    public void Camel_case_humps_count_as_word_starts()
    {
        var hump = FuzzyMatcher.Match("pr", "OpenPullRequest")!;
        Assert.Equal(new[] { 4, 8 }, hump.Indices);
    }

    [Fact]
    public void Very_long_input_is_handled_without_throwing()
    {
        var text = new string('a', 5000) + "z";
        var query = new string('a', 100);
        var m = FuzzyMatcher.Match(query, text);
        Assert.NotNull(m);
    }
}

public class PaletteRankerTests
{
    private static readonly IReadOnlyList<PaletteItem> Items = new List<PaletteItem>
    {
        new("action:refresh", PaletteKind.Action, "Refresh", Shortcut: "F5"),
        new("action:note", PaletteKind.Action, "New note"),
        new("page:reviews", PaletteKind.Page, "Review queue", Ref: "reviews"),
        new("page:settings", PaletteKind.Page, "Settings", Keywords: "preferences", Ref: "settings"),
        new("pr:p1", PaletteKind.PullRequest, "Fix flaky retry test", "acme/api #123", Keywords: "alice", Ref: "p1", Repo: "acme/api", Number: 123),
        new("pr:p2", PaletteKind.PullRequest, "Add review dashboard", "acme/web #45", Keywords: "bob", Ref: "p2", Repo: "acme/web", Number: 45),
        new("pr:p3", PaletteKind.PullRequest, "Bump deps", "acme/web #1234", Ref: "p3", Repo: "acme/web", Number: 1234),
    };

    private static List<string> Ids(IEnumerable<PaletteResult> r) => r.Select(x => x.Item.Id).ToList();

    [Fact]
    public void Empty_query_keeps_the_given_order()
    {
        Assert.Equal(Items.Select(i => i.Id).ToList(), Ids(PaletteRanker.Rank(Items, "")));
    }

    [Fact]
    public void Empty_query_puts_recent_entries_first()
    {
        var ids = Ids(PaletteRanker.Rank(Items, "", recentIds: new[] { "pr:p2", "action:note" }));
        Assert.Equal("pr:p2", ids[0]);
        Assert.Equal("action:note", ids[1]);
        Assert.Equal(Items.Count, ids.Count);
    }

    [Fact]
    public void Max_limits_the_result_count()
    {
        Assert.Equal(2, PaletteRanker.Rank(Items, "", 2).Count);
        Assert.Empty(PaletteRanker.Rank(Items, "", 0));
    }

    [Fact]
    public void Text_query_finds_by_title_and_highlights_the_title()
    {
        var results = PaletteRanker.Rank(Items, "flaky");
        var first = results[0];
        Assert.Equal("pr:p1", first.Item.Id);
        Assert.Equal(5, first.TitleMatches.Count);
    }

    [Fact]
    public void Commands_and_pages_rank_above_prs_for_the_same_word()
    {
        var ids = Ids(PaletteRanker.Rank(Items, "review"));
        Assert.Equal("page:reviews", ids[0]);
        Assert.Contains("pr:p2", ids);
    }

    [Fact]
    public void Keywords_and_repo_text_find_entries_without_highlighting_the_title()
    {
        var byKeyword = PaletteRanker.Rank(Items, "preferences");
        Assert.Equal("page:settings", byKeyword[0].Item.Id);
        Assert.Empty(byKeyword[0].TitleMatches);

        var byRepo = PaletteRanker.Rank(Items, "acme/web");
        Assert.Contains("pr:p2", Ids(byRepo));
        Assert.DoesNotContain("pr:p1", Ids(byRepo));
    }

    [Fact]
    public void Hash_number_finds_the_exact_pr_first_then_prefixes()
    {
        var ids = Ids(PaletteRanker.Rank(Items, "#123"));
        Assert.Equal(new[] { "pr:p1", "pr:p3" }, ids);
    }

    [Fact]
    public void Plain_digits_also_find_a_pr_by_number()
    {
        Assert.Equal("pr:p2", PaletteRanker.Rank(Items, "45")[0].Item.Id);
    }

    [Fact]
    public void Repo_hash_number_narrows_to_that_repo()
    {
        var ids = Ids(PaletteRanker.Rank(Items, "web#45"));
        Assert.Equal(new[] { "pr:p2" }, ids);
        Assert.Empty(PaletteRanker.Rank(Items, "api#45"));
    }

    [Fact]
    public void Leading_hash_limits_to_pull_requests()
    {
        var ids = Ids(PaletteRanker.Rank(Items, "#review"));
        Assert.All(ids, id => Assert.StartsWith("pr:", id));
        Assert.Contains("pr:p2", ids);
    }

    [Fact]
    public void Leading_greater_than_limits_to_commands_and_pages()
    {
        var ids = Ids(PaletteRanker.Rank(Items, ">review"));
        Assert.Equal(new[] { "page:reviews" }, ids);
        var all = Ids(PaletteRanker.Rank(Items, ">"));
        Assert.DoesNotContain(all, id => id.StartsWith("pr:", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_matching_query_gives_no_results()
    {
        Assert.Empty(PaletteRanker.Rank(Items, "zzzzqq"));
    }

    [Fact]
    public void Equal_scores_keep_the_original_order()
    {
        var items = new List<PaletteItem>
        {
            new("a", PaletteKind.Action, "Open"),
            new("b", PaletteKind.Action, "Open"),
        };
        Assert.Equal(new[] { "a", "b" }, Ids(PaletteRanker.Rank(items, "open")));
    }

    [Fact]
    public void Palette_items_come_from_the_snapshot_without_duplicates()
    {
        var own = new PullRequest("p1", PrRole.Authored, "acme/api", 7, "Own", "u", "me", false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 1, 1, ReviewDecision.None, CiState.Success, MergeState.Mergeable, 0, 0, "b");
        var review = own with { Id = "p2", Role = PrRole.ReviewRequested, Number = 8, Title = "Theirs", Author = "bob" };
        var snap = new HubSnapshot("me", new[] { own }, new[] { review, own }, new Dictionary<string, ReviewInfo>(), Array.Empty<FeedItem>(), DateTimeOffset.UnixEpoch);
        var items = PaletteItems.FromSnapshot(snap);
        Assert.Equal(new[] { "pr:p1", "pr:p2" }, items.Select(i => i.Id).ToList());
        Assert.Equal("acme/api #7", items[0].Subtitle);
        Assert.Equal(8, items[1].Number);
        Assert.Equal("bob", items[1].Keywords);
    }
}

public class NavigationAndChordTests
{
    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 0)]
    [InlineData(0, -1, 3, 2)]
    [InlineData(5, 1, 3, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(-4, -1, 3, 2)]
    public void Selection_wraps(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, PaletteNavigation.Move(current, delta, count));
    }

    [Fact]
    public void Clamp_keeps_the_index_in_range()
    {
        Assert.Equal(0, PaletteNavigation.Clamp(-1, 5));
        Assert.Equal(4, PaletteNavigation.Clamp(9, 5));
        Assert.Equal(0, PaletteNavigation.Clamp(3, 0));
    }

    [Fact]
    public void G_then_a_known_key_completes_a_chord()
    {
        var m = new ChordMatcher();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(ChordOutcome.Pending, m.Feed('g', t).Outcome);
        var done = m.Feed('R', t.AddMilliseconds(300));
        Assert.Equal(ChordOutcome.Completed, done.Outcome);
        Assert.Equal("reviews", done.Target);
    }

    [Fact]
    public void Keys_outside_a_chord_are_ignored()
    {
        var m = new ChordMatcher();
        Assert.Equal(ChordOutcome.None, m.Feed('o', DateTimeOffset.UnixEpoch).Outcome);
    }

    [Fact]
    public void An_unknown_second_key_cancels_the_chord()
    {
        var m = new ChordMatcher();
        m.Feed('g', DateTimeOffset.UnixEpoch);
        Assert.Equal(ChordOutcome.Cancelled, m.Feed('x', DateTimeOffset.UnixEpoch.AddMilliseconds(100)).Outcome);
        Assert.False(m.IsPending(DateTimeOffset.UnixEpoch.AddMilliseconds(150)));
    }

    [Fact]
    public void A_chord_times_out()
    {
        var m = new ChordMatcher();
        m.Feed('g', DateTimeOffset.UnixEpoch);
        var late = DateTimeOffset.UnixEpoch + ChordMatcher.Timeout + TimeSpan.FromMilliseconds(1);
        Assert.False(m.IsPending(late));
        Assert.Equal(ChordOutcome.None, m.Feed('o', late).Outcome);
    }

    [Fact]
    public void Pressing_g_twice_keeps_waiting()
    {
        var m = new ChordMatcher();
        m.Feed('g', DateTimeOffset.UnixEpoch);
        Assert.Equal(ChordOutcome.Pending, m.Feed('g', DateTimeOffset.UnixEpoch.AddMilliseconds(200)).Outcome);
        Assert.Equal("notes", m.Feed('n', DateTimeOffset.UnixEpoch.AddMilliseconds(400)).Target);
    }

    [Fact]
    public void Every_default_chord_is_listed_in_the_help()
    {
        foreach (var b in ChordMatcher.Default)
            Assert.Contains(ShortcutCatalog.All, e => e.Keys == "G then " + char.ToUpperInvariant(b.Key) && e.What == b.Label);
        Assert.Contains(ShortcutCatalog.All, e => e.Keys == "Ctrl+K");
        Assert.Contains("Go to", ShortcutCatalog.Groups);
    }
}
