using Cshubben.Core.Features.Feed;
using Cshubben.Core.Model;
using Xunit;
using static Cshubben.Core.Tests.FeedTests.FeedTestData;

namespace Cshubben.Core.Tests.FeedTests;

public class FeedGroupingTests
{
    [Fact]
    public void Consecutive_events_on_one_pr_become_one_group()
    {
        var items = new[]
        {
            Item("1", FeedKind.Pushed, hoursAgo: 1, detail: "feat: 2 commits"),
            Item("2", FeedKind.Commented, hoursAgo: 2),
            Item("3", FeedKind.Pushed, hoursAgo: 3, detail: "feat: 1 commit"),
        };
        var groups = FeedGrouping.Group(items, null);
        var group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        Assert.True(group.IsCollapsed);
        Assert.Equal("1", group.Key);
    }

    [Fact]
    public void Interleaved_prs_are_not_merged()
    {
        var items = new[]
        {
            Item("1", number: 1, hoursAgo: 1),
            Item("2", number: 2, hoursAgo: 2),
            Item("3", number: 1, hoursAgo: 3),
        };
        Assert.Equal(3, FeedGrouping.Group(items, null).Count);
    }

    [Fact]
    public void Same_number_in_another_repo_is_a_different_pr()
    {
        var items = new[] { Item("1", repo: "acme/api", number: 5), Item("2", repo: "acme/web", number: 5) };
        Assert.Equal(2, FeedGrouping.Group(items, null).Count);
    }

    [Fact]
    public void Summary_counts_commits_and_comments()
    {
        var items = new[]
        {
            Item("1", FeedKind.Commented, hoursAgo: 1, detail: "left 1 comment"),
            Item("2", FeedKind.Pushed, hoursAgo: 2, detail: "feat/x: 2 commits"),
            Item("3", FeedKind.Commented, hoursAgo: 3, detail: "left 1 comment"),
            Item("4", FeedKind.Pushed, hoursAgo: 4, detail: "feat/x: 1 commit"),
        };
        var group = Assert.Single(FeedGrouping.Group(items, null));
        Assert.Equal("maria pushed 3 commits, 2 comments", group.Summary);
    }

    [Fact]
    public void Comments_alone_read_as_left_n_comments()
    {
        var items = new[]
        {
            Item("1", FeedKind.Commented, hoursAgo: 1, detail: "left 3 comments"),
            Item("2", FeedKind.Commented, hoursAgo: 2, detail: "Looks fine to me"),
        };
        Assert.Equal("maria left 4 comments", Assert.Single(FeedGrouping.Group(items, null)).Summary);
    }

    [Fact]
    public void Single_events_get_plain_phrases()
    {
        Assert.Equal("maria approved", FeedGrouping.Group(new[] { Item("1", FeedKind.Approved) }, null)[0].Summary);
        Assert.Equal("maria commented", FeedGrouping.Group(new[] { Item("1", FeedKind.Commented, detail: "Nice") }, null)[0].Summary);
        Assert.Equal("maria pushed 1 commit", FeedGrouping.Group(new[] { Item("1", FeedKind.Pushed, detail: "main") }, null)[0].Summary);
        Assert.Equal("maria pushed 2 commits", FeedGrouping.Group(new[] { Item("1", FeedKind.Pushed, detail: "main: 2 commits") }, null)[0].Summary);
        Assert.Equal("maria requested changes", FeedGrouping.Group(new[] { Item("1", FeedKind.ChangesRequested) }, null)[0].Summary);
    }

    [Fact]
    public void Actor_label_lists_one_two_or_many_actors()
    {
        var one = new[] { Item("1", actor: "maria") };
        var two = new[] { Item("1", actor: "maria"), Item("2", actor: "jonas") };
        var many = new[] { Item("1", actor: "maria"), Item("2", actor: "jonas"), Item("3", actor: "priya"), Item("4", actor: "tom"), Item("5", actor: "Maria") };
        Assert.Equal("maria", FeedGrouping.ActorLabelOf(one));
        Assert.Equal("maria and jonas", FeedGrouping.ActorLabelOf(two));
        Assert.Equal("maria, jonas +2", FeedGrouping.ActorLabelOf(many));
    }

    [Fact]
    public void Events_without_a_pr_number_stand_alone()
    {
        var items = new[]
        {
            Item("1", FeedKind.Deployed, number: null, actor: "deploy-bot", hoursAgo: 1),
            Item("2", FeedKind.Deployed, number: null, actor: "deploy-bot", hoursAgo: 2),
        };
        Assert.Equal(2, FeedGrouping.Group(items, null).Count);
    }

    [Fact]
    public void New_and_seen_events_of_one_pr_never_share_a_group()
    {
        var items = new[] { Item("1", hoursAgo: 1), Item("2", hoursAgo: 2), Item("3", hoursAgo: 6) };
        var groups = FeedGrouping.Group(items, Ago(4));
        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsNew);
        Assert.Equal(2, groups[0].Count);
        Assert.False(groups[1].IsNew);
    }

    [Fact]
    public void A_long_gap_splits_a_group()
    {
        var items = new[] { Item("1", hoursAgo: 1), Item("2", hoursAgo: 20) };
        Assert.Equal(2, FeedGrouping.Group(items, null).Count);
        Assert.Single(FeedGrouping.Group(items, null, TimeSpan.FromHours(30)));
    }

    [Fact]
    public void The_most_significant_kind_picks_the_icon()
    {
        var items = new[]
        {
            Item("1", FeedKind.Pushed, hoursAgo: 1),
            Item("2", FeedKind.CiFailed, hoursAgo: 2, actor: "ci"),
            Item("3", FeedKind.Commented, hoursAgo: 3),
        };
        Assert.Equal(FeedKind.CiFailed, Assert.Single(FeedGrouping.Group(items, null)).Kind);
    }

    [Fact]
    public void Snippet_is_only_set_for_single_events_with_real_text()
    {
        var comment = FeedGrouping.Group(new[] { Item("1", FeedKind.Commented, detail: "Please rename this") }, null)[0];
        Assert.Equal("Please rename this", comment.Snippet);
        var counted = FeedGrouping.Group(new[] { Item("2", FeedKind.Commented, detail: "left 3 comments") }, null)[0];
        Assert.Equal("", counted.Snippet);
        var approved = FeedGrouping.Group(new[] { Item("3", FeedKind.Approved, detail: "approved") }, null)[0];
        Assert.Equal("", approved.Snippet);
        var merged = FeedGrouping.Group(new[] { Item("4", FeedKind.Pushed, detail: "main: 1 commit") }, null)[0];
        Assert.Equal("", merged.Snippet);
    }

    [Fact]
    public void Group_flags_follow_its_events()
    {
        var items = new[] { Item("1", personal: false, bot: true), Item("2", personal: true, bot: true, hoursAgo: 2) };
        var group = Assert.Single(FeedGrouping.Group(items, null));
        Assert.True(group.Personal);
        Assert.True(group.IsBot);
    }

    [Fact]
    public void Nothing_is_new_without_a_last_seen_time()
    {
        Assert.False(FeedGrouping.IsNew(Item("1", hoursAgo: 0.1), null));
        Assert.True(FeedGrouping.IsNew(Item("1", hoursAgo: 1), Ago(2)));
        Assert.False(FeedGrouping.IsNew(Item("1", hoursAgo: 3), Ago(2)));
    }
}

public class FeedLayoutTests
{
    [Fact]
    public void New_events_come_first_under_a_header_followed_by_a_divider()
    {
        var items = new[]
        {
            Item("1", number: 1, hoursAgo: 1),
            Item("2", number: 2, hoursAgo: 2),
            Item("3", number: 3, hoursAgo: 6),
        };
        var layout = FeedLayout.Build(items, Ago(3), Now, TimeZoneInfo.Utc);

        Assert.Equal(2, layout.NewEvents);
        Assert.Equal(2, layout.NewGroups);
        Assert.True(layout.HasNew);
        Assert.Equal(
            new[] { FeedEntryKind.NewHeader, FeedEntryKind.Group, FeedEntryKind.Group, FeedEntryKind.Divider, FeedEntryKind.DayHeader, FeedEntryKind.Group },
            layout.Entries.Select(e => e.Kind));
        Assert.Equal(2, layout.Entries[0].Count);
    }

    [Fact]
    public void Without_new_events_only_day_headers_appear()
    {
        var items = new[] { Item("1", number: 1, hoursAgo: 1), Item("2", number: 2, hoursAgo: 30) };
        var layout = FeedLayout.Build(items, Ago(0.5), Now, TimeZoneInfo.Utc);

        Assert.False(layout.HasNew);
        Assert.Equal(
            new[] { FeedEntryKind.DayHeader, FeedEntryKind.Group, FeedEntryKind.DayHeader, FeedEntryKind.Group },
            layout.Entries.Select(e => e.Kind));
        Assert.Equal("Today", layout.Entries[0].Label);
        Assert.Equal("Yesterday", layout.Entries[2].Label);
    }

    [Fact]
    public void Everything_new_has_no_divider()
    {
        var items = new[] { Item("1", number: 1, hoursAgo: 1), Item("2", number: 2, hoursAgo: 2) };
        var layout = FeedLayout.Build(items, Ago(10), Now, TimeZoneInfo.Utc);
        Assert.DoesNotContain(layout.Entries, e => e.Kind == FeedEntryKind.Divider);
        Assert.Equal(2, layout.NewEvents);
    }

    [Fact]
    public void No_last_seen_means_nothing_is_new()
    {
        var items = new[] { Item("1", hoursAgo: 1) };
        var layout = FeedLayout.Build(items, null, Now, TimeZoneInfo.Utc);
        Assert.Equal(0, layout.NewEvents);
        Assert.False(layout.HasNew);
    }

    [Fact]
    public void Empty_input_gives_an_empty_layout()
    {
        var layout = FeedLayout.Build(Array.Empty<FeedItem>(), Ago(1), Now, TimeZoneInfo.Utc);
        Assert.True(layout.IsEmpty);
        Assert.Empty(layout.Entries);
    }

    [Fact]
    public void Day_labels_name_today_yesterday_and_older_days()
    {
        Assert.Equal("Today", FeedLayout.DayLabel(Ago(2), Now, TimeZoneInfo.Utc));
        Assert.Equal("Yesterday", FeedLayout.DayLabel(Ago(15), Now, TimeZoneInfo.Utc));
        Assert.Equal("Monday 16 Nov", FeedLayout.DayLabel(Ago(24 * 4), Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Day_header_counts_events_of_that_day()
    {
        var items = new[] { Item("1", number: 1, hoursAgo: 1), Item("2", number: 1, hoursAgo: 2), Item("3", number: 2, hoursAgo: 3) };
        var layout = FeedLayout.Build(items, null, Now, TimeZoneInfo.Utc);
        Assert.Equal(3, layout.Entries[0].Count);
    }
}

public class FeedSeenStoreTests
{
    [Fact]
    public void In_memory_store_returns_what_was_saved()
    {
        var store = new InMemoryFeedSeenStore();
        Assert.Null(store.Load());
        store.Save(Now);
        Assert.Equal(Now, store.Load());
        Assert.Equal(Ago(1), new InMemoryFeedSeenStore(Ago(1)).Load());
    }

    [Fact]
    public void File_store_round_trips_across_instances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = FileFeedSeenStore.DefaultPath(dir);
            var first = new FileFeedSeenStore(path);
            Assert.Null(first.Load());
            first.Save(Now);
            Assert.Equal(Now, first.Load());
            Assert.Equal(Now, new FileFeedSeenStore(path).Load());
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void File_store_ignores_a_damaged_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = FileFeedSeenStore.DefaultPath(dir);
            File.WriteAllText(path, "{ not json");
            Assert.Null(new FileFeedSeenStore(path).Load());
            File.WriteAllText(path, "{\"lastSeen\": 5}");
            Assert.Null(new FileFeedSeenStore(path).Load());
            File.WriteAllText(path, "{\"lastSeen\": \"yesterday-ish\"}");
            Assert.Null(new FileFeedSeenStore(path).Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
