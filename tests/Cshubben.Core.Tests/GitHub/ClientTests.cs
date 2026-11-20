using System.Net;
using System.Text.Json;
using Cshubben.Core.Data.GitHub;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.GitHubTests;

public class ClientTests
{
    [Fact]
    public async Task Search_follows_the_cursor_until_there_are_no_more_pages()
    {
        var handler = new FakeHandler(r =>
        {
            var after = r.Variables.TryGetProperty("after", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            return after switch
            {
                null => FakeHandler.Ok(Setup.Page(new[] { "p1", "p2" }, true, "C1")),
                "C1" => FakeHandler.Ok(Setup.Page(new[] { "p3" }, true, "C2")),
                _ => FakeHandler.Ok(Setup.Page(new[] { "p4" }, false, "C3")),
            };
        });
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));

        var result = await prs.SearchAsync(PrQueries.AuthoredSearch, PrRole.Authored);

        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, result.Select(p => p.Id).ToArray());
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(JsonValueKind.Null, handler.Requests[0].Variables.GetProperty("after").ValueKind);
        Assert.Equal("C1", handler.Requests[1].Variables.GetProperty("after").GetString());
        Assert.Equal("C2", handler.Requests[2].Variables.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Search_sends_the_authored_query_with_sort_and_page_size()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(Setup.Page(Array.Empty<string>(), false, null)));
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));

        await prs.SearchAsync(PrQueries.ReviewSearch, PrRole.ReviewRequested, pageSize: 25);

        var vars = handler.Requests[0].Variables;
        Assert.StartsWith(PrQueries.ReviewSearch, vars.GetProperty("q").GetString());
        Assert.Equal(25, vars.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Search_stops_at_max_pages_even_if_github_keeps_saying_there_is_more()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(Setup.Page(new[] { "x" }, true, "SAME")));
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));

        var result = await prs.SearchAsync(PrQueries.AuthoredSearch, PrRole.Authored, maxPages: 2);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Search_stops_when_has_next_page_but_no_cursor()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(Setup.Page(new[] { "x" }, true, null)));
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));
        await prs.SearchAsync(PrQueries.AuthoredSearch, PrRole.Authored);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Requests_carry_bearer_token_and_user_agent_and_never_leak_the_token_in_errors()
    {
        var handler = new FakeHandler(_ => FakeHandler.Status(HttpStatusCode.Unauthorized));
        var gql = new GitHubGraphQl(Setup.Rest(handler, "ghp_super_secret"));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => gql.QueryAsync("query { viewer { login } }"));

        Assert.Equal(401, ex.StatusCode);
        Assert.DoesNotContain("ghp_super_secret", ex.Message);
        Assert.Equal("Bearer ghp_super_secret", handler.Requests[0].Authorization);
        Assert.Contains("Cshubben", handler.Requests[0].UserAgent);
        Assert.Equal("https://api.github.com/graphql", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Fact]
    public async Task A_502_is_retried_once()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? FakeHandler.Status(HttpStatusCode.BadGateway)
            : FakeHandler.Ok("{\"data\":{\"viewer\":{\"login\":\"alex\"}}}"));
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));

        Assert.Equal("alex", await prs.GetViewerAsync());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_503_twice_gives_up_after_one_retry()
    {
        var handler = new FakeHandler(_ => FakeHandler.Status(HttpStatusCode.ServiceUnavailable));
        var gql = new GitHubGraphQl(Setup.Rest(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => gql.QueryAsync("query { viewer { login } }"));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Graphql_errors_become_a_typed_exception()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok("{\"errors\":[{\"message\":\"Field x doesn't exist\"},{\"message\":\"Other\"}]}"));
        var gql = new GitHubGraphQl(Setup.Rest(handler));

        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(() => gql.QueryAsync("query { x }"));

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains("Field x doesn't exist", ex.Message);
        Assert.False(ex.IsRateLimited);
    }

    [Fact]
    public async Task Rate_limited_graphql_error_is_flagged()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok("{\"errors\":[{\"type\":\"RATE_LIMITED\",\"message\":\"API rate limit exceeded\"}]}"));
        var gql = new GitHubGraphQl(Setup.Rest(handler));
        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(() => gql.QueryAsync("query { x }"));
        Assert.True(ex.IsRateLimited);
    }

    [Fact]
    public async Task Partial_answers_are_accepted_only_when_asked_for()
    {
        var body = "{\"data\":{\"nodes\":[null]},\"errors\":[{\"message\":\"Could not resolve\"}]}";
        var strict = new GitHubGraphQl(Setup.Rest(new FakeHandler(_ => FakeHandler.Ok(body))));
        var lenient = new GitHubGraphQl(Setup.Rest(new FakeHandler(_ => FakeHandler.Ok(body))));

        await Assert.ThrowsAsync<GitHubGraphQlException>(() => strict.QueryAsync("q", null, false));
        var data = await lenient.QueryAsync("q", null, true);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("nodes").ValueKind);
    }

    [Fact]
    public async Task Rate_limit_is_parsed_from_headers_and_from_the_graphql_field()
    {
        var handler = new FakeHandler(_ =>
        {
            var resp = FakeHandler.Ok("{\"data\":{\"rateLimit\":{\"cost\":1,\"remaining\":4990,\"limit\":5000,\"resetAt\":\"2026-11-20T11:00:00Z\"}}}");
            resp.Headers.Add("x-ratelimit-remaining", "4989");
            resp.Headers.Add("x-ratelimit-limit", "5000");
            resp.Headers.Add("x-ratelimit-reset", "1795000000");
            return resp;
        });
        var rest = Setup.Rest(handler);
        var gql = new GitHubGraphQl(rest);

        await gql.QueryAsync("query { rateLimit { cost } }");

        Assert.Equal(4989, rest.LastRateLimit!.Remaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000), rest.LastRateLimit.ResetAt);
        Assert.Equal(4990, gql.LastRateLimit!.Remaining);
        Assert.Equal(1, gql.LastRateLimit.Cost);
    }

    [Fact]
    public async Task Enrichment_failure_for_one_chunk_leaves_prs_unenriched_and_reports_an_error()
    {
        var handler = new FakeHandler(_ => FakeHandler.Status(HttpStatusCode.InternalServerError));
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;

        var (result, errors) = await prs.EnrichAsync(new[] { pr });

        Assert.Single(result);
        Assert.Equal(CiState.Unknown, result[0].Ci);
        Assert.Single(errors);
    }

    [Fact]
    public async Task Enrichment_batches_ids_in_chunks_of_twenty_and_keeps_order()
    {
        var handler = new FakeHandler(r =>
        {
            var ids = r.Variables.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!).ToArray();
            var nodes = string.Join(",", ids.Select(id => "{\"id\":\"" + id + "\",\"reviewDecision\":\"APPROVED\"}"));
            return FakeHandler.Ok("{\"data\":{\"nodes\":[" + nodes + "]}}");
        });
        var prs = new GitHubPullRequests(new GitHubGraphQl(Setup.Rest(handler)));
        var list = Enumerable.Range(1, 45)
            .Select(i => PrMapper.FromListNode(Setup.Parse("{\"id\":\"P" + i + "\",\"number\":" + i + "}"), PrRole.Authored)!)
            .ToList();

        var (result, errors) = await prs.EnrichAsync(list);

        Assert.Empty(errors);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(list.Select(p => p.Id), result.Select(p => p.Id));
        Assert.All(result, p => Assert.Equal(ReviewDecision.Approved, p.Review));
    }
}
