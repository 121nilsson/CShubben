namespace Cshubben.Core.Tests.GitHubTests;

public static class Fixtures
{
    public const string ListNode = """
        {
          "id": "PR_abc", "number": 412, "title": "Add idempotency keys", "url": "https://github.com/acme/api/pull/412",
          "body": "Why\nand how", "isDraft": true,
          "createdAt": "2026-11-18T08:00:00Z", "updatedAt": "2026-11-20T09:30:00Z",
          "headRefName": "feat/idempotency", "additions": 212, "deletions": 34, "changedFiles": 9,
          "author": { "login": "alex" }, "repository": { "nameWithOwner": "acme/api" },
          "comments": { "totalCount": 4 }
        }
        """;

    public const string EnrichNode = """
        {
          "id": "PR_abc", "reviewDecision": "CHANGES_REQUESTED", "mergeable": "CONFLICTING",
          "reviewThreads": { "nodes": [ { "isResolved": false }, { "isResolved": true }, { "isResolved": false } ] },
          "commits": { "nodes": [ { "commit": { "statusCheckRollup": { "state": "FAILURE" } } } ] }
        }
        """;

    public const string ReviewInfoNode = """
        {
          "id": "PR_r1",
          "reviewRequests": { "nodes": [
            { "requestedReviewer": { "__typename": "Team", "name": "platform" } },
            { "requestedReviewer": { "__typename": "User", "login": "Alex" } }
          ] },
          "timelineItems": { "nodes": [ { "createdAt": "2026-11-10T10:00:00Z", "requestedReviewer": { "__typename": "Team", "name": "platform" } } ] },
          "latestReviews": { "nodes": [
            { "author": { "login": "maria" }, "state": "APPROVED", "submittedAt": "2026-11-19T10:00:00Z" },
            { "author": { "login": "alex" }, "state": "CHANGES_REQUESTED", "submittedAt": "2026-11-15T10:00:00Z" }
          ] },
          "commits": { "nodes": [ { "commit": { "committedDate": "2026-11-17T12:00:00Z" } } ] }
        }
        """;

    public static string Event(string type, string payload, string actor = "maria", string id = "100", string at = "2026-11-20T10:00:00Z") =>
        "{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"actor\":{\"login\":\"" + actor + "\"},\"created_at\":\"" + at + "\",\"payload\":" + payload + "}";

    public static string ReviewEvent(string state, string? body, string prAuthor = "someone", string actor = "maria", string id = "101")
    {
        var b = body is null ? "null" : "\"" + body + "\"";
        return Event("PullRequestReviewEvent",
            "{\"action\":\"created\",\"review\":{\"state\":\"" + state + "\",\"body\":" + b + "},\"pull_request\":{\"number\":7,\"user\":{\"login\":\"" + prAuthor + "\"}}}",
            actor, id);
    }
}
