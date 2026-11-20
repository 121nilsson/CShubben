using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cshubben.Core.Shared;
using Cshubben.Core.Features.Linear;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class LinearLinksTests
{
    [Theory]
    [InlineData("feat/eng-123-foo", "ENG-123")]
    [InlineData("feature/ENG-123", "ENG-123")]
    [InlineData("ENG-123: fix the thing", "ENG-123")]
    [InlineData("fix: [A51-1472] Move filter", "A51-1472")]
    [InlineData("chore (SHA-832) clean cache", "SHA-832")]
    [InlineData("Fix UTF-8 and ENG-5", "ENG-5")]
    [InlineData("ENG-1, ENG-2", "ENG-1")]
    [InlineData("(VOY-1004).", "VOY-1004")]
    [InlineData("Support UTF-8 in the parser", null)]
    [InlineData("feature/utf-8-fix", null)]
    [InlineData("feat/sha-256-update", null)]
    [InlineData("Use SHA-256 hashing", null)]
    [InlineData("Follow ISO-8601 dates", null)]
    [InlineData("Fixes CVE-2024-1234", null)]
    [InlineData("fix eng-123 later", null)]
    [InlineData("Eng-123", null)]
    [InlineData("xENG-123", null)]
    [InlineData("ENG-1234567", null)]
    [InlineData("ENG-12x", null)]
    [InlineData("release-2024", null)]
    [InlineData("hotfix/fix-12", null)]
    [InlineData("", null)]
    public void FindIdentifier_cases(string text, string? expected)
    {
        Assert.Equal(expected, LinearLinks.FindIdentifier(text));
    }

    [Fact]
    public void Title_wins_over_branch()
    {
        Assert.Equal("ENG-1", LinearLinks.FindIdentifier("ENG-1 thing", "feat/eng-2-x"));
        Assert.Equal("ENG-9", LinearLinks.FindIdentifier("No id here", "feat/eng-9-x"));
        Assert.Null(LinearLinks.FindIdentifier(null, null));
    }
}

public class LinearMappingTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Maps_a_full_issue_from_a_fixture()
    {
        var node = Parse("""
            {
              "id": "abc-1",
              "identifier": "ENG-123",
              "title": "Fix login redirect",
              "url": "https://linear.app/acme/issue/ENG-123/fix-login",
              "priority": 2,
              "updatedAt": "2026-11-19T10:30:00.000Z",
              "completedAt": "2026-11-20T08:00:00.000Z",
              "state": { "name": "Done", "type": "completed" },
              "assignee": { "name": "Alex Example", "displayName": "alex" },
              "team": { "key": "ENG" },
              "project": { "name": "Auth" },
              "labels": { "nodes": [ { "name": "bug" }, { "name": "auth" } ] }
            }
            """);
        var issue = LinearMapper.ParseIssue(node)!;
        Assert.Equal("ENG-123", issue.Identifier);
        Assert.Equal("Done", issue.StateName);
        Assert.Equal(LinearStateType.Completed, issue.StateType);
        Assert.True(issue.IsDone);
        Assert.Equal("alex", issue.Assignee);
        Assert.Equal(2, issue.Priority);
        Assert.Equal("High", issue.PriorityLabel);
        Assert.Equal("ENG", issue.TeamKey);
        Assert.Equal("Auth", issue.Project);
        Assert.Equal(new[] { "bug", "auth" }, issue.Labels);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 30, 0, TimeSpan.Zero), issue.UpdatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), issue.CompletedAt);
    }

    [Fact]
    public void Missing_optional_parts_do_not_break_mapping()
    {
        var node = Parse("{\"id\":\"1\",\"identifier\":\"ENG-1\",\"title\":\"T\",\"priority\":99,\"assignee\":null,\"project\":null,\"completedAt\":null}");
        var issue = LinearMapper.ParseIssue(node)!;
        Assert.Null(issue.Assignee);
        Assert.Null(issue.Project);
        Assert.Null(issue.CompletedAt);
        Assert.Equal(0, issue.Priority);
        Assert.Equal(LinearStateType.Unknown, issue.StateType);
        Assert.Empty(issue.Labels);
    }

    [Fact]
    public void Node_without_identifier_is_skipped()
    {
        Assert.Null(LinearMapper.ParseIssue(Parse("{\"id\":\"1\"}")));
    }

    [Fact]
    public void Issue_page_reads_cursor_and_skips_bad_nodes()
    {
        var json = Parse("{\"nodes\":[" + Fx.IssueJson("1", "ENG-1", "Todo", "unstarted") + ",{\"id\":\"x\"}],\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":\"c1\"}}");
        var page = LinearMapper.ParseIssuePage(json);
        Assert.Single(page.Items);
        Assert.True(page.HasNextPage);
        Assert.Equal("c1", page.EndCursor);
    }

    [Fact]
    public void Viewer_falls_back_to_email_when_name_is_missing()
    {
        var data = Parse("{\"viewer\":{\"id\":\"u1\",\"name\":\"\",\"email\":\"a@example.com\"}}");
        var viewer = LinearMapper.ParseViewer(data);
        Assert.Equal("a@example.com", viewer.Name);
        Assert.Equal("u1", viewer.Id);
    }

    [Fact]
    public void Teams_and_labels_are_mapped()
    {
        var teams = LinearMapper.ParseTeamPage(Parse("{\"nodes\":[{\"id\":\"t1\",\"key\":\"ENG\",\"name\":\"Engineering\"},{\"id\":\"t2\"}],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}"));
        Assert.Equal(new LinearTeam("t1", "ENG", "Engineering"), Assert.Single(teams.Items));
        var labels = LinearMapper.ParseLabels(Parse("{\"nodes\":[{\"id\":\"l1\",\"name\":\"bug\"}]}"));
        Assert.Equal(new LinearLabel("l1", "bug"), Assert.Single(labels));
    }

    [Fact]
    public void Authentication_error_becomes_a_typed_exception()
    {
        var root = Parse("{\"errors\":[{\"message\":\"Authentication required, not authenticated\",\"extensions\":{\"type\":\"authentication error\"}}]}");
        Assert.Throws<LinearAuthException>(() => LinearMapper.Unwrap(root));
    }

    [Fact]
    public void Ratelimited_error_becomes_a_rate_limit_exception_with_retry_after()
    {
        var root = Parse("{\"errors\":[{\"message\":\"Too many requests\",\"extensions\":{\"code\":\"RATELIMITED\"}}]}");
        var ex = Assert.Throws<LinearRateLimitException>(() => LinearMapper.Unwrap(root, TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromSeconds(12), ex.RetryAfter);
    }

    [Fact]
    public void Other_errors_keep_their_messages_and_not_found_is_flagged()
    {
        var root = Parse("{\"errors\":[{\"message\":\"Entity not found: Issue\"}]}");
        var ex = Assert.Throws<LinearApiException>(() => LinearMapper.Unwrap(root));
        Assert.True(ex.IsNotFound);
        Assert.Contains("Entity not found", ex.Message);
        var other = Assert.Throws<LinearApiException>(() => LinearMapper.Unwrap(Parse("{\"errors\":[{\"message\":\"Boom\"}]}")));
        Assert.False(other.IsNotFound);
    }

    [Fact]
    public void Response_without_data_is_an_error()
    {
        Assert.Throws<LinearApiException>(() => LinearMapper.Unwrap(Parse("{}")));
    }
}

public class LinearQueriesTests
{
    [Fact]
    public void My_issues_filters_out_done_states_and_omits_a_null_cursor()
    {
        var request = LinearQueries.MyIssues(50, null);
        Assert.Contains("assignedIssues", request.Query);
        Assert.Contains("\"completed\"", request.Query);
        Assert.Contains("\"canceled\"", request.Query);
        Assert.Equal(50, request.Variables["first"]);
        Assert.False(request.Variables.ContainsKey("after"));
        Assert.Equal("abc", LinearQueries.MyIssues(10, "abc").Variables["after"]);
    }

    [Fact]
    public void Recently_completed_embeds_a_fixed_format_timestamp()
    {
        var request = LinearQueries.RecentlyCompleted(new DateTimeOffset(2026, 11, 1, 12, 30, 5, TimeSpan.FromHours(2)), 20);
        Assert.Contains("completedAt: { gte: \"2026-11-01T10:30:05Z\" }", request.Query);
    }

    [Fact]
    public void Create_issue_input_leaves_out_empty_fields()
    {
        var request = LinearQueries.CreateIssue("team1", "Title", "  ", null, Array.Empty<string>());
        var input = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(request.Variables["input"]);
        Assert.Equal("team1", input["teamId"]);
        Assert.Equal("Title", input["title"]);
        Assert.False(input.ContainsKey("description"));
        Assert.False(input.ContainsKey("priority"));
        Assert.False(input.ContainsKey("labelIds"));

        var full = LinearQueries.CreateIssue("team1", "Title", "Body", 3, new[] { "l1" });
        var fullInput = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(full.Variables["input"]);
        Assert.Equal(3, fullInput["priority"]);
        Assert.Equal("Body", fullInput["description"]);
    }

    [Fact]
    public void Issue_by_identifier_passes_the_identifier_as_a_variable()
    {
        Assert.Equal("ENG-1", LinearQueries.IssueByIdentifier("ENG-1").Variables["id"]);
    }
}

public class LinearClientTests
{
    private const string Key = "lin_api_SECRETKEY123";

    private static LinearClient Client(FakeHandler handler, string? key = Key) =>
        new(new HttpClient(handler), () => key, (_, _) => Task.CompletedTask);

    private const string ViewerOk = "{\"data\":{\"viewer\":{\"id\":\"u1\",\"name\":\"Alex\",\"email\":\"a@example.com\"}}}";

    [Fact]
    public async Task Sends_the_raw_key_without_bearer_to_the_graphql_endpoint()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, ViewerOk);
        var viewer = await Client(handler).GetViewerAsync();

        Assert.Equal("Alex", viewer.Name);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(Key, request.Authorization);
        Assert.Equal(LinearClient.Endpoint, request.Url);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Contains("viewer", body.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task Missing_key_fails_without_any_request()
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<LinearAuthException>(() => Client(handler, key: " ").GetViewerAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Retries_once_on_a_server_error()
    {
        var handler = new FakeHandler()
            .Enqueue(HttpStatusCode.BadGateway, "bad gateway")
            .Enqueue(HttpStatusCode.OK, ViewerOk);
        var viewer = await Client(handler).GetViewerAsync();
        Assert.Equal("u1", viewer.Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_second_server_error()
    {
        var handler = new FakeHandler()
            .Enqueue(HttpStatusCode.InternalServerError, "x")
            .Enqueue(HttpStatusCode.ServiceUnavailable, "y");
        var ex = await Assert.ThrowsAsync<LinearHttpException>(() => Client(handler).GetViewerAsync());
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Unauthorized_is_an_auth_exception_that_does_not_leak_the_key()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.Unauthorized, "{}");
        var ex = await Assert.ThrowsAsync<LinearAuthException>(() => Client(handler).GetViewerAsync());
        Assert.DoesNotContain("SECRETKEY", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_429_is_a_rate_limit_exception_with_retry_after()
    {
        var handler = new FakeHandler().Enqueue(
            (HttpStatusCode)429,
            "{}",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));
        var ex = await Assert.ThrowsAsync<LinearRateLimitException>(() => Client(handler).GetViewerAsync());
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task Graphql_ratelimited_error_on_http_400_is_a_rate_limit_exception()
    {
        var handler = new FakeHandler().Enqueue(
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"message\":\"Rate limit exceeded\",\"extensions\":{\"code\":\"RATELIMITED\"}}]}");
        await Assert.ThrowsAsync<LinearRateLimitException>(() => Client(handler).GetViewerAsync());
    }

    [Fact]
    public async Task Rate_limit_headers_are_remembered()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, ViewerOk, r =>
        {
            r.Headers.TryAddWithoutValidation("X-RateLimit-Requests-Remaining", "1234");
            r.Headers.TryAddWithoutValidation("X-RateLimit-Requests-Reset", "1800000000000");
        });
        var client = Client(handler);
        Assert.Null(client.LastRateLimit);
        await client.GetViewerAsync();
        Assert.Equal(1234, client.LastRateLimit!.RequestsRemaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1800000000000), client.LastRateLimit.ResetsAt);
    }

    [Fact]
    public async Task My_issues_follows_pages_until_done()
    {
        var handler = new FakeHandler()
            .Enqueue(HttpStatusCode.OK, Fx.MyIssuesResponse(Fx.IssueJson("1", "ENG-1", "Todo", "unstarted"), true, "cursor-1"))
            .Enqueue(HttpStatusCode.OK, Fx.MyIssuesResponse(Fx.IssueJson("2", "ENG-2", "In Progress", "started"), false, null));
        var issues = await Client(handler).GetMyIssuesAsync();

        Assert.Equal(new[] { "ENG-1", "ENG-2" }, issues.Select(i => i.Identifier));
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("cursor-1", handler.Requests[0].Body);
        Assert.Contains("cursor-1", handler.Requests[1].Body);
    }

    [Fact]
    public async Task My_issues_stops_at_the_requested_maximum()
    {
        var handler = new FakeHandler()
            .Enqueue(HttpStatusCode.OK, Fx.MyIssuesResponse(Fx.IssueJson("1", "ENG-1", "Todo", "unstarted") + "," + Fx.IssueJson("2", "ENG-2", "Todo", "unstarted"), true, "c"));
        var issues = await Client(handler).GetMyIssuesAsync(maxIssues: 2);
        Assert.Equal(2, issues.Count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Issue_by_identifier_returns_the_issue_or_null_when_not_found()
    {
        var found = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"data\":{\"issue\":" + Fx.IssueJson("1", "ENG-1", "Todo", "unstarted") + "}}");
        Assert.Equal("ENG-1", (await Client(found).GetIssueByIdentifierAsync("ENG-1"))!.Identifier);

        var missing = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"errors\":[{\"message\":\"Entity not found: Issue\"}],\"data\":null}");
        Assert.Null(await Client(missing).GetIssueByIdentifierAsync("ENG-999"));
    }

    [Fact]
    public async Task Recently_completed_and_teams_and_labels_work()
    {
        var done = Fx.IssueJson("1", "ENG-1", "Done", "completed", "\"2026-11-19T09:00:00.000Z\"");
        var handler = new FakeHandler()
            .Enqueue(HttpStatusCode.OK, Fx.MyIssuesResponse(done, false, null))
            .Enqueue(HttpStatusCode.OK, "{\"data\":{\"teams\":{\"nodes\":[{\"id\":\"t1\",\"key\":\"ENG\",\"name\":\"Engineering\"}],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}")
            .Enqueue(HttpStatusCode.OK, "{\"data\":{\"team\":{\"labels\":{\"nodes\":[{\"id\":\"l1\",\"name\":\"bug\"}]}}}}");
        var client = Client(handler);

        var completed = await client.GetRecentlyCompletedAsync(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.NotNull(completed[0].CompletedAt);
        Assert.Equal("ENG", (await client.GetTeamsAsync())[0].Key);
        Assert.Equal("bug", (await client.GetTeamLabelsAsync("t1"))[0].Name);
    }

    [Fact]
    public async Task Create_issue_sends_the_input_and_returns_the_new_issue()
    {
        var response = "{\"data\":{\"issueCreate\":{\"success\":true,\"issue\":" + Fx.IssueJson("9", "ENG-9", "Backlog", "backlog") + "}}}";
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, response);
        var issue = await Client(handler).CreateIssueAsync("team-1", "  New thing ", "Body", 3, new[] { "label-1" });

        Assert.Equal("ENG-9", issue.Identifier);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var input = body.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("team-1", input.GetProperty("teamId").GetString());
        Assert.Equal("New thing", input.GetProperty("title").GetString());
        Assert.Equal(3, input.GetProperty("priority").GetInt32());
        Assert.Equal("label-1", input.GetProperty("labelIds")[0].GetString());
    }

    [Fact]
    public async Task Create_issue_reports_failure_and_validates_input()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"data\":{\"issueCreate\":{\"success\":false,\"issue\":null}}}");
        await Assert.ThrowsAsync<LinearApiException>(() => Client(handler).CreateIssueAsync("t", "title"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).CreateIssueAsync("t", " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(handler).CreateIssueAsync("t", "x", null, 7));
    }

    [Fact]
    public async Task Link_pull_request_creates_an_attachment()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"data\":{\"attachmentCreate\":{\"success\":true,\"attachment\":{\"id\":\"a1\",\"url\":\"u\"}}}}");
        await Client(handler).LinkPullRequestAsync("issue-1", "https://github.com/acme/api/pull/5", "Fix login");

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Contains("attachmentCreate", body.RootElement.GetProperty("query").GetString());
        var input = body.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("issue-1", input.GetProperty("issueId").GetString());
        Assert.Equal("https://github.com/acme/api/pull/5", input.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Link_pull_request_refuses_non_github_urls()
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).LinkPullRequestAsync("i", "https://evil.example/x", "t"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Api_key_resolution_reads_the_secret_store()
    {
        var store = new InMemorySecretStore();
        store.Set(SecretKeys.LinearApiKey, " k ");
        var old = Environment.GetEnvironmentVariable("LINEAR_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("LINEAR_API_KEY", null);
            Assert.Equal("k", LinearClient.ResolveApiKey(store));
            Assert.Null(LinearClient.ResolveApiKey(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LINEAR_API_KEY", old);
        }
    }
}

public class LinearReconciliationTests
{
    private static ReconPr Open(int number, string title, string branch = "feat/x") =>
        new("acme/api", number, title, "https://github.com/acme/api/pull/" + number, branch, false);

    private static ReconPr Merged(int number, string title) =>
        new("acme/api", number, title, "https://github.com/acme/api/pull/" + number, "feat/x", true, Fx.T);

    private static LinearIssue Issue(string id, string state, LinearStateType type) => Fx.Issue(id, state, type);

    [Fact]
    public void Merged_pr_with_open_issue_is_flagged()
    {
        var items = LinearReconciliation.Compute(
            new[] { Merged(7, "ENG-1 add thing") },
            new[] { Issue("ENG-1", "In Review", LinearStateType.Started) });
        var item = Assert.Single(items);
        Assert.Equal(ReconKind.MergedButIssueOpen, item.Kind);
        Assert.Contains("ENG-1", item.Text);
        Assert.Contains("In Review", item.Text);
    }

    [Fact]
    public void Open_pr_with_done_or_canceled_issue_is_flagged()
    {
        var items = LinearReconciliation.Compute(
            new[] { Open(1, "ENG-1 a"), Open(2, "ENG-2 b") },
            new[] { Issue("ENG-1", "Done", LinearStateType.Completed), Issue("ENG-2", "Canceled", LinearStateType.Canceled) });
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(ReconKind.IssueDoneButPrOpen, i.Kind));
    }

    [Fact]
    public void Open_pr_without_identifier_is_flagged_but_a_merged_one_is_not()
    {
        var items = LinearReconciliation.Compute(new[] { Open(3, "Tidy up"), Merged(4, "Tidy up more") }, Array.Empty<LinearIssue>());
        var item = Assert.Single(items);
        Assert.Equal(ReconKind.PrWithoutIssue, item.Kind);
        Assert.Equal(3, item.Pr!.Number);
    }

    [Fact]
    public void Identifier_in_the_branch_name_counts_as_a_link()
    {
        var items = LinearReconciliation.Compute(
            new[] { Open(5, "Tidy up", "feat/eng-4-tidy") },
            new[] { Issue("ENG-4", "In Progress", LinearStateType.Started) });
        Assert.Empty(items);
    }

    [Fact]
    public void In_progress_issue_without_any_pr_is_flagged()
    {
        var items = LinearReconciliation.Compute(
            Array.Empty<ReconPr>(),
            new[] { Issue("ENG-8", "In Progress", LinearStateType.Started), Issue("ENG-9", "Todo", LinearStateType.Unstarted) });
        var item = Assert.Single(items);
        Assert.Equal(ReconKind.InProgressWithoutPr, item.Kind);
        Assert.Equal("ENG-8", item.Issue!.Identifier);
    }

    [Fact]
    public void In_progress_issue_with_an_open_pr_is_fine()
    {
        var items = LinearReconciliation.Compute(
            new[] { Open(6, "ENG-8 work") },
            new[] { Issue("ENG-8", "In Progress", LinearStateType.Started) });
        Assert.Empty(items);
    }

    [Fact]
    public void Unknown_issue_identifier_is_not_flagged()
    {
        var items = LinearReconciliation.Compute(new[] { Merged(7, "ENG-404 gone") }, Array.Empty<LinearIssue>());
        Assert.Empty(items);
    }

    [Fact]
    public void Merged_pr_with_done_issue_is_fine()
    {
        var items = LinearReconciliation.Compute(
            new[] { Merged(7, "ENG-1 add thing") },
            new[] { Issue("ENG-1", "Done", LinearStateType.Completed) });
        Assert.Empty(items);
    }

    [Fact]
    public void Results_are_ordered_by_kind()
    {
        var items = LinearReconciliation.Compute(
            new[] { Open(10, "No id"), Open(11, "ENG-2 b"), Merged(12, "ENG-1 a") },
            new[]
            {
                Issue("ENG-1", "In Progress", LinearStateType.Started),
                Issue("ENG-2", "Done", LinearStateType.Completed),
                Issue("ENG-3", "In Progress", LinearStateType.Started),
            });
        Assert.Equal(
            new[] { ReconKind.MergedButIssueOpen, ReconKind.IssueDoneButPrOpen, ReconKind.PrWithoutIssue, ReconKind.InProgressWithoutPr },
            items.Select(i => i.Kind));
    }

    [Fact]
    public void FromOpen_copies_the_branch_and_title()
    {
        var pr = ReconPr.FromOpen(Fx.Pr("ENG-3 title", "feat/eng-3-x", 42));
        Assert.False(pr.Merged);
        Assert.Equal(42, pr.Number);
        Assert.Equal("feat/eng-3-x", pr.Branch);
    }
}
