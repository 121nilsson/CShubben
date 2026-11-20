using Cshubben.Core.Model;
using System.Text.Json;

namespace Cshubben.Core.Data.GitHub;

/// <summary>Pure mapping of one REST repo event to a <see cref="FeedItem"/>, with the noise filter built in.</summary>
public static class FeedMapper
{
    private sealed record Parsed(FeedKind Kind, int Number, string? Title, string Detail, string? PrAuthor, string? Reviewer);

    /// <summary>
    /// Returns null for noise: stars, forks, branch/tag creation and deletion, unsupported event types,
    /// reviews without a verdict or text (they only accompany inline comments), edited or deleted comments,
    /// closed-without-merge PRs, comments on plain issues.
    /// Personal means: the viewer is the actor, the PR author or the requested reviewer, or the PR is in myPrKeys.
    /// </summary>
    public static FeedItem? Map(string repo, JsonElement ev, string viewer, IReadOnlySet<string>? myPrKeys = null)
    {
        var id = J.Str(ev, "id");
        var at = J.Time(ev, "created_at");
        if (id.Length == 0 || at is null) return null;

        var type = J.Str(ev, "type");
        var payload = J.At(ev, "payload") ?? default(JsonElement);
        var parsed = type switch
        {
            "PullRequestEvent" => ParsePullRequest(payload),
            "PullRequestReviewEvent" => ParseReview(payload),
            "PullRequestReviewCommentEvent" => ParseReviewComment(payload),
            "IssueCommentEvent" => ParseIssueComment(payload),
            "PushEvent" => ParsePush(payload),
            _ => null,
        };
        if (parsed is null) return null;

        var actor = J.Str(ev, "actor", "login");
        var personal = Same(actor, viewer)
            || Same(parsed.PrAuthor, viewer)
            || Same(parsed.Reviewer, viewer)
            || (parsed.Number > 0 && myPrKeys is not null && myPrKeys.Contains(GitHubNames.PrKey(repo, parsed.Number)));

        return new FeedItem(
            Id: "gh:" + id,
            Kind: parsed.Kind,
            Repo: repo,
            Actor: actor,
            Number: parsed.Number > 0 ? parsed.Number : null,
            Title: parsed.Title,
            Detail: parsed.Detail,
            At: at.Value,
            Personal: personal,
            IsBot: IsBot(actor));
    }

    public static bool IsBot(string? login) =>
        !string.IsNullOrEmpty(login) && login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

    /// <summary>First non-empty line, without HTML comments and markdown noise, cut to max characters.</summary>
    public static string Snippet(string? text, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = StripHtmlComments(text).Replace("**", "").Replace("`", "");
        foreach (var raw in cleaned.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#').Trim();
            if (line.Length == 0) continue;
            return line.Length > max ? line.Substring(0, max) + "…" : line;
        }
        return string.Empty;
    }

    private static string StripHtmlComments(string s)
    {
        var sb = new System.Text.StringBuilder();
        var rest = s;
        while (true)
        {
            var start = rest.IndexOf("<!--", StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(rest);
                break;
            }
            sb.Append(rest, 0, start);
            var end = rest.IndexOf("-->", start, StringComparison.Ordinal);
            if (end < 0) break;
            rest = rest.Substring(end + 3);
        }
        return sb.ToString();
    }

    private static bool Same(string? a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static Parsed? ParsePullRequest(JsonElement payload)
    {
        var pr = J.At(payload, "pull_request");
        var number = J.IntOrNull(pr, "number") ?? J.IntOrNull(payload, "number");
        if (number is null) return null;
        var title = J.StrOrNull(pr, "title");
        var author = J.StrOrNull(pr, "user", "login");
        switch (J.Str(payload, "action"))
        {
            case "opened":
                return new Parsed(FeedKind.Opened, number.Value, title, string.Empty, author, null);
            case "closed":
                if (J.Bool(pr, "merged")) return new Parsed(FeedKind.Merged, number.Value, title, string.Empty, author, null);
                return null;
            case "review_requested":
                var reviewer = J.StrOrNull(payload, "requested_reviewer", "login");
                var detail = reviewer ?? J.Str(payload, "requested_team", "name");
                return new Parsed(FeedKind.ReviewRequested, number.Value, title, detail, author, reviewer);
            default:
                return null;
        }
    }

    private static Parsed? ParseReview(JsonElement payload)
    {
        var action = J.Str(payload, "action");
        if (action.Length > 0 && action != "created") return null;
        var pr = J.At(payload, "pull_request");
        var number = J.IntOrNull(pr, "number");
        if (number is null) return null;
        var body = Snippet(J.StrOrNull(payload, "review", "body"));
        FeedKind kind;
        switch (J.Str(payload, "review", "state").ToLowerInvariant())
        {
            case "approved":
                kind = FeedKind.Approved;
                break;
            case "changes_requested":
                kind = FeedKind.ChangesRequested;
                break;
            case "commented":
                if (body.Length == 0) return null;
                kind = FeedKind.Commented;
                break;
            default:
                return null;
        }
        return new Parsed(kind, number.Value, J.StrOrNull(pr, "title"), body, J.StrOrNull(pr, "user", "login"), null);
    }

    private static Parsed? ParseReviewComment(JsonElement payload)
    {
        if (J.Str(payload, "action") != "created") return null;
        var pr = J.At(payload, "pull_request");
        var number = J.IntOrNull(pr, "number");
        if (number is null) return null;
        return new Parsed(
            FeedKind.Commented,
            number.Value,
            J.StrOrNull(pr, "title"),
            Snippet(J.StrOrNull(payload, "comment", "body")),
            J.StrOrNull(pr, "user", "login"),
            null);
    }

    private static Parsed? ParseIssueComment(JsonElement payload)
    {
        if (J.Str(payload, "action") != "created") return null;
        var issue = J.At(payload, "issue");
        // Only comments on pull requests; plain issues are not part of this hub.
        if (J.At(issue, "pull_request") is null) return null;
        var number = J.IntOrNull(issue, "number");
        if (number is null) return null;
        return new Parsed(
            FeedKind.Commented,
            number.Value,
            J.StrOrNull(issue, "title"),
            Snippet(J.StrOrNull(payload, "comment", "body")),
            J.StrOrNull(issue, "user", "login"),
            null);
    }

    private static Parsed? ParsePush(JsonElement payload)
    {
        var branch = J.Str(payload, "ref");
        const string prefix = "refs/heads/";
        if (branch.StartsWith(prefix, StringComparison.Ordinal)) branch = branch.Substring(prefix.Length);
        var count = J.IntOrNull(payload, "distinct_size") ?? J.IntOrNull(payload, "size");
        if (branch.Length == 0 || count == 0) return null;
        var detail = count is null ? branch : branch + ": " + count.Value + (count.Value == 1 ? " commit" : " commits");
        return new Parsed(FeedKind.Pushed, 0, null, detail, null, null);
    }
}
