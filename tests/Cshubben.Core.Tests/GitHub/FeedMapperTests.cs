using Cshubben.Core.Data.GitHub;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.GitHubTests;

public class FeedMapperTests
{
    private static FeedItem? Map(string eventJson, string viewer = "alex", params string[] myPrKeys) =>
        FeedMapper.Map("acme/api", Setup.Parse(eventJson), viewer, new HashSet<string>(myPrKeys));

    [Theory]
    [InlineData("WatchEvent")]
    [InlineData("ForkEvent")]
    [InlineData("CreateEvent")]
    [InlineData("DeleteEvent")]
    [InlineData("MemberEvent")]
    [InlineData("PublicEvent")]
    public void Noise_event_types_are_dropped(string type) =>
        Assert.Null(Map(Fixtures.Event(type, "{\"action\":\"started\",\"ref_type\":\"branch\"}")));

    [Fact]
    public void Review_without_verdict_or_text_is_dropped()
    {
        Assert.Null(Map(Fixtures.ReviewEvent("commented", null)));
        Assert.Null(Map(Fixtures.ReviewEvent("commented", "   ")));
    }

    [Fact]
    public void Review_with_text_or_verdict_is_kept()
    {
        Assert.Equal(FeedKind.Commented, Map(Fixtures.ReviewEvent("commented", "Looks fine to me"))!.Kind);
        Assert.Equal(FeedKind.Approved, Map(Fixtures.ReviewEvent("approved", null))!.Kind);
        Assert.Equal(FeedKind.ChangesRequested, Map(Fixtures.ReviewEvent("changes_requested", "Please fix"))!.Kind);
    }

    [Fact]
    public void Dismissed_review_is_dropped() =>
        Assert.Null(Map(Fixtures.ReviewEvent("dismissed", "x")));

    [Fact]
    public void Bot_actor_is_detected_by_suffix()
    {
        var bot = Map(Fixtures.ReviewEvent("approved", null, actor: "dependabot[bot]"))!;
        var human = Map(Fixtures.ReviewEvent("approved", null, actor: "maria"))!;
        Assert.True(bot.IsBot);
        Assert.False(human.IsBot);
        Assert.True(FeedMapper.IsBot("renovate[bot]"));
        Assert.False(FeedMapper.IsBot("bottom"));
    }

    [Fact]
    public void Event_is_personal_when_the_viewer_is_the_actor()
    {
        Assert.True(Map(Fixtures.ReviewEvent("approved", null, actor: "alex"))!.Personal);
    }

    [Fact]
    public void Event_is_personal_when_it_is_on_a_pr_authored_by_the_viewer()
    {
        Assert.True(Map(Fixtures.ReviewEvent("approved", null, prAuthor: "Alex"))!.Personal);
    }

    [Fact]
    public void Event_is_personal_when_the_pr_is_in_the_viewers_set()
    {
        Assert.True(Map(Fixtures.ReviewEvent("approved", null), "alex", "acme/api#7")!.Personal);
    }

    [Fact]
    public void Unrelated_event_is_not_personal()
    {
        Assert.False(Map(Fixtures.ReviewEvent("approved", null), "alex", "acme/api#99")!.Personal);
    }

    [Fact]
    public void Review_request_for_the_viewer_is_personal_and_typed()
    {
        var json = Fixtures.Event("PullRequestEvent",
            "{\"action\":\"review_requested\",\"number\":12,\"pull_request\":{\"number\":12,\"title\":\"T\",\"user\":{\"login\":\"jonas\"}},\"requested_reviewer\":{\"login\":\"alex\"}}",
            actor: "jonas");
        var item = Map(json)!;
        Assert.Equal(FeedKind.ReviewRequested, item.Kind);
        Assert.Equal(12, item.Number);
        Assert.True(item.Personal);
    }

    [Fact]
    public void Merged_and_closed_without_merge_are_told_apart()
    {
        string Closed(bool merged) => Fixtures.Event("PullRequestEvent",
            "{\"action\":\"closed\",\"number\":5,\"pull_request\":{\"number\":5,\"merged\":" + (merged ? "true" : "false") + "}}");
        Assert.Equal(FeedKind.Merged, Map(Closed(true))!.Kind);
        Assert.Null(Map(Closed(false)));
    }

    [Fact]
    public void Opened_pr_has_title_when_the_payload_has_one_and_null_when_not()
    {
        var withTitle = Map(Fixtures.Event("PullRequestEvent", "{\"action\":\"opened\",\"number\":3,\"pull_request\":{\"number\":3,\"title\":\"Hello\"}}"))!;
        var without = Map(Fixtures.Event("PullRequestEvent", "{\"action\":\"opened\",\"number\":4,\"pull_request\":{\"number\":4}}"))!;
        Assert.Equal("Hello", withTitle.Title);
        Assert.Null(without.Title);
        Assert.Equal(4, without.Number);
    }

    [Fact]
    public void Comments_on_plain_issues_are_dropped_but_pr_comments_kept()
    {
        var issue = Fixtures.Event("IssueCommentEvent", "{\"action\":\"created\",\"issue\":{\"number\":8,\"title\":\"Bug\"},\"comment\":{\"body\":\"hi\"}}");
        var pr = Fixtures.Event("IssueCommentEvent", "{\"action\":\"created\",\"issue\":{\"number\":9,\"title\":\"Feat\",\"pull_request\":{\"url\":\"u\"}},\"comment\":{\"body\":\"**Nice** work\"}}");
        var edited = Fixtures.Event("IssueCommentEvent", "{\"action\":\"edited\",\"issue\":{\"number\":9,\"pull_request\":{}},\"comment\":{\"body\":\"x\"}}");
        Assert.Null(Map(issue));
        Assert.Null(Map(edited));
        var kept = Map(pr)!;
        Assert.Equal(FeedKind.Commented, kept.Kind);
        Assert.Equal("Nice work", kept.Detail);
        Assert.Equal("Feat", kept.Title);
    }

    [Fact]
    public void Push_event_shows_branch_and_commit_count()
    {
        var item = Map(Fixtures.Event("PushEvent", "{\"ref\":\"refs/heads/feat/x\",\"distinct_size\":3}"))!;
        Assert.Equal(FeedKind.Pushed, item.Kind);
        Assert.Equal("feat/x: 3 commits", item.Detail);
        Assert.Null(item.Number);
        Assert.Null(Map(Fixtures.Event("PushEvent", "{\"ref\":\"refs/heads/gone\",\"size\":0}")));
    }

    [Fact]
    public void Id_and_time_are_mapped_and_events_without_them_dropped()
    {
        var item = Map(Fixtures.ReviewEvent("approved", null, id: "555"))!;
        Assert.Equal("gh:555", item.Id);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), item.At);
        Assert.Null(Map("{\"type\":\"PushEvent\",\"payload\":{}}"));
    }

    [Fact]
    public void Snippet_strips_html_comments_markdown_and_truncates()
    {
        Assert.Equal("Hello world", FeedMapper.Snippet("<!-- meta -->\n\n## **Hello** `world`\nsecond line"));
        Assert.Equal("", FeedMapper.Snippet(null));
        var cut = FeedMapper.Snippet(new string('x', 200));
        Assert.Equal(141, cut.Length);
        Assert.EndsWith("…", cut);
    }

    private static FeedItem Item(string id, FeedKind kind, string actor, int? number, string at, string detail = "") =>
        new(id, kind, "acme/api", actor, number, null, detail, DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), false, false);

    [Fact]
    public void Collapse_keeps_only_the_newest_of_a_burst_and_orders_newest_first()
    {
        var items = new[]
        {
            Item("1", FeedKind.Commented, "maria", 7, "2026-11-20T10:00:00Z"),
            Item("2", FeedKind.Commented, "maria", 7, "2026-11-20T10:03:00Z"),
            Item("3", FeedKind.Commented, "maria", 7, "2026-11-20T10:06:00Z"),
            Item("4", FeedKind.Approved, "maria", 7, "2026-11-20T10:01:00Z"),
        };
        var result = FeedFilter.Collapse(items);
        Assert.Equal(new[] { "3", "4" }, result.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Collapse_keeps_items_that_are_far_apart_or_from_different_actors_and_drops_repeated_ids()
    {
        var items = new[]
        {
            Item("1", FeedKind.Commented, "maria", 7, "2026-11-20T10:00:00Z"),
            Item("2", FeedKind.Commented, "maria", 7, "2026-11-20T12:00:00Z"),
            Item("3", FeedKind.Commented, "jonas", 7, "2026-11-20T12:01:00Z"),
            Item("3", FeedKind.Commented, "jonas", 7, "2026-11-20T12:01:00Z"),
        };
        Assert.Equal(3, FeedFilter.Collapse(items).Count);
    }

    [Fact]
    public void Collapse_treats_pushes_to_different_branches_as_different()
    {
        var items = new[]
        {
            Item("1", FeedKind.Pushed, "maria", null, "2026-11-20T10:00:00Z", "a: 1 commit"),
            Item("2", FeedKind.Pushed, "maria", null, "2026-11-20T10:01:00Z", "b: 2 commits"),
            Item("3", FeedKind.Pushed, "maria", null, "2026-11-20T10:02:00Z", "b: 1 commit"),
        };
        Assert.Equal(2, FeedFilter.Collapse(items).Count);
    }
}
