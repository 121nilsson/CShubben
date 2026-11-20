using Cshubben.Core.Features.Feed;
using Cshubben.Core.Model;
using Xunit;
using static Cshubben.Core.Tests.FeedTests.FeedTestData;

namespace Cshubben.Core.Tests.FeedTests;

public class RepoFilterTests
{
    [Fact]
    public void Muted_repo_matches_case_insensitively()
    {
        Assert.True(RepoFilter.IsMuted("Acme/API", new[] { "acme/api" }, Array.Empty<string>()));
    }

    [Fact]
    public void Muted_org_mutes_every_repo_of_the_owner()
    {
        Assert.True(RepoFilter.IsMuted("noisy/one", Array.Empty<string>(), new[] { "NOISY" }));
        Assert.True(RepoFilter.IsMuted("noisy/two", null, new[] { "noisy" }));
    }

    [Fact]
    public void Unmuted_repo_is_not_muted()
    {
        Assert.False(RepoFilter.IsMuted("acme/web", new[] { "acme/api" }, new[] { "other" }));
        Assert.False(RepoFilter.IsMuted("acme/web", null, null));
    }

    [Fact]
    public void Empty_followed_list_follows_everything()
    {
        Assert.True(RepoFilter.IsFollowed("acme/api", Array.Empty<string>()));
        Assert.True(RepoFilter.IsFollowed("acme/api", null));
    }

    [Fact]
    public void Followed_list_matches_repo_or_owner()
    {
        var followed = new[] { "acme/api", "tools" };
        Assert.True(RepoFilter.IsFollowed("ACME/api", followed));
        Assert.True(RepoFilter.IsFollowed("tools/cli", followed));
        Assert.False(RepoFilter.IsFollowed("acme/web", followed));
    }

    [Fact]
    public void OwnerOf_handles_names_without_a_slash()
    {
        Assert.Equal("acme", RepoFilter.OwnerOf("acme/api"));
        Assert.Equal("solo", RepoFilter.OwnerOf("solo"));
    }

    [Fact]
    public void FilterSnapshot_removes_muted_prs_infos_and_feed()
    {
        var snap = Snapshot(
            new[] { Pr("a1", "acme/api"), Pr("a2", "noisy/x") },
            new[] { Pr("r1", "acme/web", PrRole.ReviewRequested), Pr("r2", "noisy/y", PrRole.ReviewRequested) },
            new[] { Item("f1", repo: "acme/api"), Item("f2", repo: "noisy/x"), Item("f3", repo: "acme/muted") });

        var result = RepoFilter.FilterSnapshot(snap, new[] { "acme/muted" }, new[] { "noisy" });

        Assert.Equal(new[] { "a1" }, result.Authored.Select(p => p.Id));
        Assert.Equal(new[] { "r1" }, result.Reviews.Select(p => p.Id));
        Assert.Equal(new[] { "r1" }, result.ReviewInfos.Keys);
        Assert.Equal(new[] { "f1" }, result.Feed.Select(i => i.Id));
        Assert.Equal(snap.Viewer, result.Viewer);
        Assert.Equal(snap.At, result.At);
    }

    [Fact]
    public void FilterSnapshot_returns_the_same_instance_when_nothing_is_muted()
    {
        var snap = Snapshot(new[] { Pr("a1", "acme/api") }, Array.Empty<PullRequest>(), Array.Empty<FeedItem>());
        Assert.Same(snap, RepoFilter.FilterSnapshot(snap, Array.Empty<string>(), Array.Empty<string>()));
        Assert.Same(snap, RepoFilter.FilterSnapshot(snap, null, null));
    }
}

public class FeedFilteringTests
{
    private static readonly FeedItem[] Sample =
    {
        Item("p1", personal: true, hoursAgo: 1, number: 1),
        Item("t1", personal: false, hoursAgo: 2, number: 2),
        Item("b1", personal: false, bot: true, actor: "dependabot", hoursAgo: 3, number: 3),
        Item("p2", personal: true, hoursAgo: 4, number: 4),
    };

    [Fact]
    public void Bots_are_hidden_by_default()
    {
        var result = FeedFiltering.Apply(Sample, new FeedOptions());
        Assert.DoesNotContain(result, i => i.IsBot);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Bots_show_when_asked()
    {
        var result = FeedFiltering.Apply(Sample, new FeedOptions { ShowBots = true });
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Personal_scope_keeps_only_personal_events()
    {
        var result = FeedFiltering.Apply(Sample, new FeedOptions { Scope = FeedScope.Personal });
        Assert.Equal(new[] { "p1", "p2" }, result.Select(i => i.Id));
    }

    [Fact]
    public void Team_scope_keeps_only_non_personal_events()
    {
        var result = FeedFiltering.Apply(Sample, new FeedOptions { Scope = FeedScope.Team, ShowBots = true });
        Assert.Equal(new[] { "t1", "b1" }, result.Select(i => i.Id));
    }

    [Fact]
    public void Muted_and_followed_repos_are_applied()
    {
        var items = new[]
        {
            Item("a", repo: "acme/api"),
            Item("b", repo: "acme/web"),
            Item("c", repo: "other/x"),
        };
        var options = new FeedOptions { FollowedRepos = new[] { "acme" }, MutedRepos = new[] { "acme/web" } };
        Assert.Equal(new[] { "a" }, FeedFiltering.Apply(items, options).Select(i => i.Id));
    }

    [Fact]
    public void Result_is_newest_first_with_stable_ties()
    {
        var items = new[] { Item("b", hoursAgo: 5), Item("z", hoursAgo: 1), Item("a", hoursAgo: 1) };
        Assert.Equal(new[] { "a", "z", "b" }, FeedFiltering.Apply(items, new FeedOptions()).Select(i => i.Id));
    }

    [Fact]
    public void Counts_ignore_scope_and_report_hidden_bots()
    {
        var counts = FeedFiltering.Count(Sample, new FeedOptions { Scope = FeedScope.Personal });
        Assert.Equal(new FeedCounts(3, 2, 1, 1), counts);
    }

    [Fact]
    public void Counts_include_bots_when_shown()
    {
        var counts = FeedFiltering.Count(Sample, new FeedOptions { ShowBots = true });
        Assert.Equal(new FeedCounts(4, 2, 2, 1), counts);
    }

    [Fact]
    public void MarkAllSeenAt_is_the_newest_event_or_now()
    {
        Assert.Equal(Now, FeedFiltering.MarkAllSeenAt(Sample, Now));
        var future = new[] { Item("x", hoursAgo: -2) };
        Assert.Equal(Ago(-2), FeedFiltering.MarkAllSeenAt(future, Now));
    }
}
