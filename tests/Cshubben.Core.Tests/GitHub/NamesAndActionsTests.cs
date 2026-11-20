using System.Net;
using System.Text.Json;
using Cshubben.Core.Data.GitHub;
using Xunit;

namespace Cshubben.Core.Tests.GitHubTests;

public class NamesAndActionsTests
{
    [Theory]
    [InlineData("acme", true)]
    [InlineData("my-repo_1.x", true)]
    [InlineData(".github", true)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("a\"b", false)]
    [InlineData("ålund", false)]
    public void Name_validation(string name, bool expected) =>
        Assert.Equal(expected, GitHubNames.IsSafeName(name));

    [Theory]
    [InlineData("acme")]
    [InlineData("a/b/c")]
    [InlineData("a/..")]
    [InlineData("../b")]
    [InlineData("a b/c")]
    [InlineData("/c")]
    public void Bad_repo_names_are_rejected(string repo)
    {
        Assert.False(GitHubNames.TrySplitRepo(repo, out _, out _));
        Assert.Throws<ArgumentException>(() => GitHubActions.CommentRequest(repo, 1, "hi"));
    }

    [Fact]
    public void Good_repo_name_splits() =>
        Assert.Equal(("acme", "api"), GitHubNames.SplitRepo("acme/api"));

    [Fact]
    public void Comment_request_targets_the_issue_comments_endpoint()
    {
        var req = GitHubActions.CommentRequest("acme/api", 12, "  hello  ");
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.github.com/repos/acme/api/issues/12/comments", req.Url);
        Assert.Equal("hello", Setup.Parse(req.JsonBody!).GetProperty("body").GetString());
    }

    [Fact]
    public void Empty_comment_and_bad_number_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => GitHubActions.CommentRequest("acme/api", 1, "   "));
        Assert.Throws<ArgumentOutOfRangeException>(() => GitHubActions.CommentRequest("acme/api", 0, "hi"));
    }

    [Theory]
    [InlineData(ReviewVerdict.Approve, "APPROVE")]
    [InlineData(ReviewVerdict.RequestChanges, "REQUEST_CHANGES")]
    [InlineData(ReviewVerdict.Comment, "COMMENT")]
    public void Review_request_uses_the_right_event(ReviewVerdict verdict, string expected)
    {
        var req = GitHubActions.ReviewRequest("acme/api", 3, verdict, "text");
        Assert.Equal("https://api.github.com/repos/acme/api/pulls/3/reviews", req.Url);
        Assert.Equal(expected, Setup.Parse(req.JsonBody!).GetProperty("event").GetString());
    }

    [Fact]
    public void Approve_may_be_empty_but_other_verdicts_need_text()
    {
        var approve = GitHubActions.ReviewRequest("acme/api", 3, ReviewVerdict.Approve, null);
        Assert.False(Setup.Parse(approve.JsonBody!).TryGetProperty("body", out _));
        Assert.Throws<ArgumentException>(() => GitHubActions.ReviewRequest("acme/api", 3, ReviewVerdict.RequestChanges, ""));
        Assert.Throws<ArgumentException>(() => GitHubActions.ReviewRequest("acme/api", 3, ReviewVerdict.Comment, null));
    }

    [Fact]
    public void Mark_ready_sends_the_id_as_a_variable_only()
    {
        var req = GitHubActions.MarkReadyRequest("PR_kwDOabc123");
        Assert.Equal(GitHubGraphQl.Endpoint, req.Url);
        var root = Setup.Parse(req.JsonBody!);
        Assert.Equal("PR_kwDOabc123", root.GetProperty("variables").GetProperty("id").GetString());
        Assert.DoesNotContain("PR_kwDOabc123", root.GetProperty("query").GetString());
        Assert.Throws<ArgumentException>(() => GitHubActions.MarkReadyRequest("x\") { evil"));
    }

    [Fact]
    public void Rerun_failed_jobs_url()
    {
        var req = GitHubActions.RerunFailedJobsRequest("acme/api", 987654321L);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.github.com/repos/acme/api/actions/runs/987654321/rerun-failed-jobs", req.Url);
        Assert.Throws<ArgumentOutOfRangeException>(() => GitHubActions.RerunFailedJobsRequest("acme/api", 0));
    }

    [Fact]
    public void Merge_request_carries_method_and_expected_sha()
    {
        var sha = "0123456789abcdef0123456789abcdef01234567";
        var req = GitHubActions.MergeRequest("acme/api", 5, GitHubMergeMethod.Squash, sha);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal("https://api.github.com/repos/acme/api/pulls/5/merge", req.Url);
        var body = Setup.Parse(req.JsonBody!);
        Assert.Equal("squash", body.GetProperty("merge_method").GetString());
        Assert.Equal(sha, body.GetProperty("sha").GetString());
        Assert.Throws<ArgumentException>(() => GitHubActions.MergeRequest("acme/api", 5, GitHubMergeMethod.Merge, "not-a-sha"));
    }

    [Fact]
    public void Head_sha_is_parsed_and_validated()
    {
        Assert.Equal("abc1234", GitHubActions.ParseHeadSha(Setup.Parse("{\"head\":{\"sha\":\"abc1234\"}}")));
        Assert.Null(GitHubActions.ParseHeadSha(Setup.Parse("{\"head\":{\"sha\":\"zzzz\"}}")));
        Assert.Null(GitHubActions.ParseHeadSha(Setup.Parse("{}")));
    }

    [Fact]
    public async Task Merge_reads_the_head_sha_first_and_then_merges_with_it()
    {
        var sha = "0123456789abcdef0123456789abcdef01234567";
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get
            ? FakeHandler.Ok("{\"head\":{\"sha\":\"" + sha + "\"}}")
            : FakeHandler.Ok("{\"merged\":true,\"sha\":\"m1\"}"));
        var writer = new GitHubWriter(Setup.Rest(handler));

        var result = await writer.MergeAsync("acme/api", 5, GitHubMergeMethod.Merge, dryRun: false);

        Assert.True(result.Sent);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(sha, JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Dry_run_merge_does_not_send_the_write()
    {
        var sha = "0123456789abcdef0123456789abcdef01234567";
        var handler = new FakeHandler(_ => FakeHandler.Ok("{\"head\":{\"sha\":\"" + sha + "\"}}"));
        var writer = new GitHubWriter(Setup.Rest(handler));

        var result = await writer.MergeAsync("acme/api", 5, GitHubMergeMethod.Squash, dryRun: true);

        Assert.False(result.Sent);
        Assert.Equal(HttpMethod.Put, result.Request.Method);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Dry_run_actions_send_nothing()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok("{}"));
        var writer = new GitHubWriter(Setup.Rest(handler));

        var a = await writer.CommentAsync("acme/api", 1, "hi", dryRun: true);
        var b = await writer.SubmitReviewAsync("acme/api", 1, ReviewVerdict.Approve, null, dryRun: true);
        var c = await writer.MarkReadyAsync("PR_abc", dryRun: true);
        var d = await writer.RerunFailedJobsAsync("acme/api", 77, dryRun: true);

        Assert.All(new[] { a, b, c, d }, r => Assert.False(r.Sent));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Real_comment_is_posted_once_even_when_github_answers_502()
    {
        var handler = new FakeHandler(_ => FakeHandler.Status(HttpStatusCode.BadGateway));
        var writer = new GitHubWriter(Setup.Rest(handler));

        await Assert.ThrowsAsync<GitHubApiException>(() => writer.CommentAsync("acme/api", 1, "hi", dryRun: false));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Mark_ready_surfaces_graphql_errors()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok("{\"errors\":[{\"message\":\"Not allowed\"}]}"));
        var writer = new GitHubWriter(Setup.Rest(handler));
        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(() => writer.MarkReadyAsync("PR_abc", dryRun: false));
        Assert.Contains("Not allowed", ex.Message);
    }
}
