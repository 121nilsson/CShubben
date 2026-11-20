using System.Net;
using System.Text.Json;
using Cshubben.Core.Data.GitHub;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.GitHubTests;

public class FeedAndHubDataTests
{
    private static GitHubFeed Feed(FakeHandler handler)
    {
        var rest = Setup.Rest(handler);
        return new GitHubFeed(rest, new GitHubGraphQl(rest));
    }

    private static string OneEventList(string title = "\"title\":\"Hello\",") =>
        "[" + Fixtures.Event("PullRequestEvent", "{\"action\":\"opened\",\"number\":3,\"pull_request\":{\"number\":3," + title + "\"user\":{\"login\":\"maria\"}}}") + "]";

    [Fact]
    public async Task Etag_is_sent_on_the_second_call_and_a_304_reuses_the_cached_events()
    {
        var handler = new FakeHandler(r => r.IfNoneMatch is null
            ? FakeHandler.Ok(OneEventList(), "\"etag-1\"")
            : FakeHandler.Status(HttpStatusCode.NotModified));
        var feed = Feed(handler);
        var keys = new HashSet<string>();

        var first = await feed.FetchAsync(new[] { "acme/api" }, "alex", keys);
        var second = await feed.FetchAsync(new[] { "acme/api" }, "alex", keys);

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Null(handler.Requests[0].IfNoneMatch);
        Assert.Equal("\"etag-1\"", handler.Requests[1].IfNoneMatch);
        Assert.Contains("/repos/acme/api/events", handler.Requests[0].Url);
    }

    [Fact]
    public async Task Etags_are_kept_per_repo()
    {
        var handler = new FakeHandler(r => r.Url.Contains("/acme/api/")
            ? FakeHandler.Ok(OneEventList(), "\"api\"")
            : FakeHandler.Ok(OneEventList(), "\"web\""));
        var feed = Feed(handler);
        var keys = new HashSet<string>();

        await feed.FetchAsync(new[] { "acme/api", "acme/web" }, "alex", keys);
        await feed.FetchAsync(new[] { "acme/api", "acme/web" }, "alex", keys);

        var second = handler.Requests.Skip(2).ToList();
        Assert.Equal("\"api\"", second.Single(r => r.Url.Contains("/acme/api/")).IfNoneMatch);
        Assert.Equal("\"web\"", second.Single(r => r.Url.Contains("/acme/web/")).IfNoneMatch);
    }

    [Fact]
    public async Task One_failing_repo_does_not_stop_the_others()
    {
        var handler = new FakeHandler(r => r.Url.Contains("/acme/bad/")
            ? FakeHandler.Status(HttpStatusCode.Forbidden)
            : FakeHandler.Ok(OneEventList()));
        var feed = Feed(handler);

        var result = await feed.FetchAsync(new[] { "acme/api", "acme/bad", "not a repo" }, "alex", new HashSet<string>());

        Assert.Single(result.Items);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task Missing_titles_are_looked_up_with_one_aliased_call_per_30_prs_and_cached()
    {
        var events = new List<string>();
        for (var i = 1; i <= 35; i++)
        {
            events.Add(Fixtures.Event("PullRequestEvent",
                "{\"action\":\"opened\",\"number\":" + i + ",\"pull_request\":{\"number\":" + i + "}}",
                actor: "actor" + i, id: "e" + i));
        }
        var eventsJson = "[" + string.Join(",", events) + "]";
        var handler = new FakeHandler(r =>
        {
            if (r.Url.Contains("/events")) return FakeHandler.Ok(eventsJson);
            // Answer every alias of the query with a title.
            var aliases = System.Text.RegularExpressions.Regex.Matches(r.Query, @"p\d+(?=: repository)")
                .Select(m => "\"" + m.Value + "\":{\"issueOrPullRequest\":{\"title\":\"Title " + m.Value + "\"}}");
            return FakeHandler.Ok("{\"data\":{" + string.Join(",", aliases) + "}}");
        });
        var feed = Feed(handler);

        var first = await feed.FetchAsync(new[] { "acme/api" }, "alex", new HashSet<string>());
        var graphqlCalls = handler.Requests.Count(r => r.Url.EndsWith("/graphql"));

        Assert.Equal(2, graphqlCalls);
        Assert.Equal(35, first.Items.Count);
        Assert.All(first.Items, i => Assert.StartsWith("Title p", i.Title));

        // Second load: events are fetched again, but titles come from the cache.
        await feed.FetchAsync(new[] { "acme/api" }, "alex", new HashSet<string>());
        Assert.Equal(2, handler.Requests.Count(r => r.Url.EndsWith("/graphql")));
    }

    [Fact]
    public async Task Seeded_titles_avoid_any_lookup()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(OneEventList("")));
        var feed = Feed(handler);
        feed.SeedTitle("acme/api", 3, "Known title");

        var result = await feed.FetchAsync(new[] { "acme/api" }, "alex", new HashSet<string>());

        Assert.Equal("Known title", result.Items[0].Title);
        Assert.DoesNotContain(handler.Requests, r => r.Url.EndsWith("/graphql"));
    }

    private static HttpResponseMessage Route(Recorded r, bool feedFails)
    {
        if (r.Url.Contains("/events"))
        {
            return feedFails ? FakeHandler.Status(HttpStatusCode.InternalServerError) : FakeHandler.Ok("[]");
        }
        var q = r.Query;
        if (q.Contains("search("))
        {
            var search = r.Variables.GetProperty("q").GetString() ?? "";
            return search.Contains("author:@me")
                ? FakeHandler.Ok("{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[" + Fixtures.ListNode + "]}}}")
                : FakeHandler.Ok("{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[{\"id\":\"PR_r1\",\"number\":7,\"title\":\"Review me\",\"repository\":{\"nameWithOwner\":\"acme/web\"},\"author\":{\"login\":\"maria\"}}]}}}");
        }
        if (q.Contains("latestReviews")) return FakeHandler.Ok("{\"data\":{\"nodes\":[" + Fixtures.ReviewInfoNode + "]}}");
        if (q.Contains("reviewThreads")) return FakeHandler.Ok("{\"data\":{\"nodes\":[" + Fixtures.EnrichNode + "]}}");
        if (q.Contains("viewer")) return FakeHandler.Ok("{\"data\":{\"viewer\":{\"login\":\"alex\"}}}");
        return FakeHandler.Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Snapshot_combines_lists_enrichment_and_review_infos()
    {
        var handler = new FakeHandler(r => Route(r, feedFails: false));
        var data = new GitHubHubData(Setup.Rest(handler));

        var snapshot = await data.LoadAsync();

        Assert.Equal("alex", snapshot.Viewer);
        Assert.Single(snapshot.Authored);
        Assert.Single(snapshot.Reviews);
        Assert.Equal(CiState.Failure, snapshot.Authored[0].Ci);
        Assert.Equal(ReviewDecision.ChangesRequested, snapshot.Authored[0].Review);
        Assert.True(snapshot.ReviewInfos.ContainsKey("PR_r1"));
        Assert.Empty(data.LastWarnings);
    }

    [Fact]
    public async Task Feed_failure_still_returns_the_prs_with_a_warning()
    {
        var handler = new FakeHandler(r => Route(r, feedFails: true));
        var data = new GitHubHubData(Setup.Rest(handler));

        var snapshot = await data.LoadAsync();

        Assert.Single(snapshot.Authored);
        Assert.Single(snapshot.Reviews);
        Assert.Empty(snapshot.Feed);
        Assert.NotEmpty(data.LastWarnings);
    }

    [Fact]
    public async Task Failing_pr_search_fails_the_load()
    {
        var handler = new FakeHandler(r => r.Query.Contains("search(") ? FakeHandler.Status(HttpStatusCode.Unauthorized) : Route(r, false));
        var data = new GitHubHubData(Setup.Rest(handler));
        await Assert.ThrowsAsync<GitHubApiException>(() => data.LoadAsync());
    }

    [Fact]
    public void Titles_query_skips_unsafe_names_and_numbers()
    {
        var q = PrQueries.BuildTitlesQuery(new[] { ("acme/api", 1), ("acme/..", 2), ("a\"b/c", 3), ("acme/web", 0), ("acme/web", 5) });
        Assert.Equal(2, q.Aliases.Count);
        Assert.Contains("repository(owner: \"acme\", name: \"api\")", q.Query);
        Assert.Contains("issueOrPullRequest(number: 5)", q.Query);
        Assert.DoesNotContain("name: \"..\"", q.Query);
    }
}
