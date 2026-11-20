using System.Text.Json;
using Cshubben.Core.Features.Pipeline;
using Xunit;
using static Cshubben.Core.Tests.Features.Pipeline.PipelineTestData;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class ConversationTests
{
    private static ConversationItem Item(string id, ConversationKind kind, string author, double hoursAgo, ReviewStateKind review = ReviewStateKind.None, bool resolved = true, bool bot = false) =>
        new(id, kind, author, bot, Ago(hoursAgo), "text", "") { Review = review, Resolved = resolved, Title = kind == ConversationKind.Commit ? "msg " + id : "" };

    [Fact]
    public void Items_are_ordered_oldest_first()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("b", ConversationKind.Comment, "x", 1),
            Item("a", ConversationKind.Comment, "y", 5),
        }, "abc1234");
        Assert.Equal(new[] { "a", "b" }, c.Items.Select(i => i.Id).ToArray());
        Assert.Equal("abc1234", c.HeadSha);
    }

    [Fact]
    public void Consecutive_commits_by_one_author_are_grouped()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("c1", ConversationKind.Commit, "alex", 5),
            Item("c2", ConversationKind.Commit, "alex", 4),
            Item("c3", ConversationKind.Commit, "alex", 3),
            Item("k", ConversationKind.Comment, "maria", 2),
            Item("c4", ConversationKind.Commit, "alex", 1),
        }, "");
        Assert.Equal(3, c.Items.Count);
        Assert.Equal(3, c.Items[0].Count);
        Assert.Equal("msg c3", c.Items[0].Title);
        Assert.Equal(1, c.Items[2].Count);
        Assert.Equal("pushed 3 commits", ConversationBuilder.Describe(c.Items[0]));
    }

    [Fact]
    public void Commits_by_different_authors_are_not_grouped()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("c1", ConversationKind.Commit, "alex", 5),
            Item("c2", ConversationKind.Commit, "maria", 4),
        }, "");
        Assert.Equal(2, c.Items.Count);
    }

    [Fact]
    public void Unresolved_threads_are_counted()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("t1", ConversationKind.Thread, "maria", 5, resolved: false),
            Item("t2", ConversationKind.Thread, "maria", 4, resolved: true),
            Item("t3", ConversationKind.Thread, "jonas", 3, resolved: false),
        }, "");
        Assert.Equal(2, c.UnresolvedThreads);
        Assert.Contains("2 unresolved threads", c.Summary);
    }

    [Fact]
    public void Latest_review_per_author_decides_approvals()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("r1", ConversationKind.Review, "priya", 9, ReviewStateKind.ChangesRequested),
            Item("r2", ConversationKind.Review, "priya", 5, ReviewStateKind.Approved),
            Item("r3", ConversationKind.Review, "jonas", 4, ReviewStateKind.Approved),
            Item("r4", ConversationKind.Review, "jonas", 3, ReviewStateKind.Commented),
            Item("r5", ConversationKind.Review, "sofia", 2, ReviewStateKind.ChangesRequested),
        }, "");
        Assert.Equal(new[] { "priya", "jonas" }, c.Approvers.ToArray());
        Assert.Equal(new[] { "sofia" }, c.ChangesRequestedBy.ToArray());
    }

    [Fact]
    public void Dismissed_review_no_longer_counts()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("r1", ConversationKind.Review, "priya", 9, ReviewStateKind.Approved),
            Item("r2", ConversationKind.Review, "priya", 5, ReviewStateKind.Dismissed),
        }, "");
        Assert.Empty(c.Approvers);
    }

    [Fact]
    public void Author_names_compare_case_insensitively()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("r1", ConversationKind.Review, "Priya", 9, ReviewStateKind.ChangesRequested),
            Item("r2", ConversationKind.Review, "priya", 5, ReviewStateKind.Approved),
        }, "");
        Assert.Single(c.Approvers);
        Assert.Empty(c.ChangesRequestedBy);
    }

    [Fact]
    public void Visible_can_hide_commits_and_events()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("c", ConversationKind.Commit, "alex", 5),
            Item("e", ConversationKind.Event, "alex", 4),
            Item("k", ConversationKind.Comment, "maria", 3),
        }, "");
        Assert.Equal(3, c.Visible(true).Count);
        Assert.Equal("k", Assert.Single(c.Visible(false)).Id);
    }

    [Fact]
    public void Bot_comments_do_not_count_as_comments()
    {
        var c = ConversationBuilder.Build(new[]
        {
            Item("k1", ConversationKind.Comment, "codecov[bot]", 3, bot: true),
            Item("k2", ConversationKind.Comment, "maria", 2),
        }, "");
        Assert.Equal(1, c.CommentCount);
        Assert.StartsWith("1 comment", c.Summary);
    }

    [Fact]
    public void Clean_body_removes_control_characters_and_caps_length()
    {
        Assert.Equal("a\nb", ConversationBuilder.CleanBody("a\r\nb\u0007"));
        var long1 = ConversationBuilder.CleanBody(new string('x', 10000));
        Assert.True(long1.Length <= ConversationBuilder.MaxBodyLength + 1);
        Assert.EndsWith("…", long1);
        Assert.Equal(string.Empty, ConversationBuilder.CleanBody(null));
    }

    [Fact]
    public void Graphql_answer_is_mapped_to_a_conversation()
    {
        var data = JsonDocument.Parse("""
            { "repository": { "pullRequest": {
              "headRefOid": "a1b2c3d4e5f6",
              "comments": { "nodes": [
                { "id": "C1", "body": "Looks good", "createdAt": "2026-11-20T08:00:00Z", "url": "https://github.com/acme/api/pull/1#c1", "author": { "login": "maria", "__typename": "User" } },
                { "id": "C2", "body": "Coverage 91%", "createdAt": "2026-11-20T08:05:00Z", "url": "javascript:x", "author": { "login": "codecov", "__typename": "Bot" } }
              ] },
              "reviews": { "nodes": [
                { "id": "R1", "state": "APPROVED", "body": "", "submittedAt": "2026-11-20T09:00:00Z", "url": "https://github.com/acme/api/pull/1#r1", "author": { "login": "priya", "__typename": "User" } },
                { "id": "R2", "state": "COMMENTED", "body": "", "submittedAt": "2026-11-20T09:01:00Z", "author": { "login": "jonas" } },
                { "id": "R3", "state": "PENDING", "body": "draft", "author": { "login": "alex" } }
              ] },
              "commits": { "nodes": [
                { "commit": { "oid": "a1b2c3d4e5f6", "messageHeadline": "Add keys\nmore", "committedDate": "2026-11-20T07:00:00Z", "author": { "name": "Alex", "user": { "login": "alex" } } } },
                { "commit": { "oid": "nothex!", "committedDate": "2026-11-20T07:01:00Z" } }
              ] },
              "reviewThreads": { "nodes": [
                { "id": "T1", "isResolved": false, "isOutdated": false, "path": "src/A.cs", "line": 12,
                  "comments": { "nodes": [
                    { "id": "TC1", "body": "Why?", "createdAt": "2026-11-20T09:10:00Z", "author": { "login": "maria" } },
                    { "id": "TC2", "body": "Because", "createdAt": "2026-11-20T09:20:00Z", "author": { "login": "alex" } } ] } }
              ] },
              "timelineItems": { "nodes": [
                { "__typename": "LabeledEvent", "id": "E1", "createdAt": "2026-11-20T07:30:00Z", "actor": { "login": "alex" }, "label": { "name": "bug" } },
                { "__typename": "SomethingElse", "id": "E2", "createdAt": "2026-11-20T07:31:00Z" }
              ] }
            } } }
            """).RootElement.Clone();

        var c = ConversationJson.Parse(data);
        Assert.Equal("a1b2c3d4e5f6", c.HeadSha);
        // commit, label event, comment, bot comment, approval, thread  (commented review and pending review skipped)
        Assert.Equal(6, c.Items.Count);
        Assert.Equal(ConversationKind.Commit, c.Items[0].Kind);
        Assert.Equal("Add keys", c.Items[0].Title);
        Assert.Equal("added the label bug", c.Items[1].Title);
        Assert.True(c.Items[3].IsBot);
        Assert.Equal(string.Empty, c.Items[3].Url);
        Assert.Equal(new[] { "priya" }, c.Approvers.ToArray());
        var thread = c.Items[5];
        Assert.Equal(ConversationKind.Thread, thread.Kind);
        Assert.False(thread.Resolved);
        Assert.Equal("src/A.cs", thread.Path);
        Assert.Equal(12, thread.Line);
        Assert.Equal("alex", Assert.Single(thread.Replies).Author);
        Assert.Equal(1, c.UnresolvedThreads);
    }

    [Fact]
    public void Missing_pull_request_gives_an_empty_conversation()
    {
        var c = ConversationJson.Parse(JsonDocument.Parse("""{ "repository": { "pullRequest": null } }""").RootElement.Clone());
        Assert.Empty(c.Items);
    }

    [Fact]
    public void Query_uses_variables_only()
    {
        Assert.Contains("$owner", ConversationJson.Query);
        Assert.DoesNotContain("acme", ConversationJson.Query);
    }
}
