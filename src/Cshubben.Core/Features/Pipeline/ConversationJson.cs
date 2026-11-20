using System.Text.Json;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>
/// Maps the GraphQL <c>repository.pullRequest</c> answer (comments, reviews, commits, review threads, timeline events)
/// to a <see cref="PrConversation"/>. Pure and tolerant; all text is cleaned because it is untrusted.
/// </summary>
public static class ConversationJson
{
    /// <summary>
    /// The query fields expected by <see cref="Parse"/>. The owner, name and number travel as variables, never in the text.
    /// </summary>
    public const string Query = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              headRefOid
              comments(first: 100) { nodes { id body createdAt url author { login __typename } } }
              reviews(first: 100) { nodes { id state body submittedAt url author { login __typename } } }
              commits(first: 100) { nodes { commit { oid messageHeadline committedDate url author { name user { login } } } } }
              reviewThreads(first: 60) {
                nodes {
                  id isResolved isOutdated path line
                  comments(first: 30) { nodes { id body createdAt url author { login __typename } } }
                }
              }
              timelineItems(first: 60, itemTypes: [LABELED_EVENT, READY_FOR_REVIEW_EVENT, CONVERT_TO_DRAFT_EVENT, REVIEW_REQUESTED_EVENT, MERGED_EVENT, CLOSED_EVENT, REOPENED_EVENT, HEAD_REF_FORCE_PUSHED_EVENT]) {
                nodes {
                  __typename
                  ... on LabeledEvent { id createdAt actor { login __typename } label { name } }
                  ... on ReadyForReviewEvent { id createdAt actor { login __typename } }
                  ... on ConvertToDraftEvent { id createdAt actor { login __typename } }
                  ... on ReviewRequestedEvent { id createdAt actor { login __typename } requestedReviewer { ... on User { login } ... on Team { name } } }
                  ... on MergedEvent { id createdAt actor { login __typename } }
                  ... on ClosedEvent { id createdAt actor { login __typename } }
                  ... on ReopenedEvent { id createdAt actor { login __typename } }
                  ... on HeadRefForcePushedEvent { id createdAt actor { login __typename } }
                }
              }
            }
          }
        }
        """;

    /// <summary>Parses the "data" object of the answer to <see cref="Query"/>.</summary>
    public static PrConversation Parse(JsonElement? data)
    {
        var pr = Js.At(data, "repository", "pullRequest");
        if (pr is null) return PrConversation.Empty;

        var items = new List<ConversationItem>();

        foreach (var node in Js.Items(pr, "comments", "nodes"))
        {
            var at = Js.Time(node, "createdAt");
            var id = Js.Str(node, "id");
            if (at is null || id is null) continue;
            var (author, bot) = Who(node, "author");
            items.Add(new ConversationItem(id, ConversationKind.Comment, author, bot, at.Value,
                ConversationBuilder.CleanBody(Js.Str(node, "body")), PipelineJson.SafeUrl(Js.Str(node, "url"))));
        }

        foreach (var node in Js.Items(pr, "reviews", "nodes"))
        {
            var at = Js.Time(node, "submittedAt");
            var id = Js.Str(node, "id");
            var state = ReviewStateOf(Js.Str(node, "state"));
            if (at is null || id is null || state is null) continue; // pending reviews have no time
            var body = ConversationBuilder.CleanBody(Js.Str(node, "body"));
            // A plain comment review without text only carries inline comments, which are shown as threads.
            if (state == ReviewStateKind.Commented && body.Length == 0) continue;
            var (author, bot) = Who(node, "author");
            items.Add(new ConversationItem(id, ConversationKind.Review, author, bot, at.Value, body,
                PipelineJson.SafeUrl(Js.Str(node, "url"))) { Review = state.Value });
        }

        foreach (var node in Js.Items(pr, "commits", "nodes"))
        {
            var commit = Js.At(node, "commit");
            var sha = Js.Str(commit, "oid");
            var at = Js.Time(commit, "committedDate");
            if (!PipelineJson.IsSha(sha) || at is null) continue;
            var login = Js.Str(commit, "author", "user", "login") ?? Js.Str(commit, "author", "name") ?? "unknown";
            items.Add(new ConversationItem("commit-" + sha, ConversationKind.Commit, login, IsBotLogin(login), at.Value,
                string.Empty, PipelineJson.SafeUrl(Js.Str(commit, "url")))
            {
                Sha = sha,
                Title = FirstLine(Js.Str(commit, "messageHeadline")),
            });
        }

        foreach (var node in Js.Items(pr, "reviewThreads", "nodes"))
        {
            var thread = ThreadOf(node);
            if (thread is not null) items.Add(thread);
        }

        foreach (var node in Js.Items(pr, "timelineItems", "nodes"))
        {
            var ev = EventOf(node);
            if (ev is not null) items.Add(ev);
        }

        return ConversationBuilder.Build(items, Js.Str(pr, "headRefOid") ?? string.Empty);
    }

    private static ConversationItem? ThreadOf(JsonElement node)
    {
        var id = Js.Str(node, "id");
        var comments = Js.Items(node, "comments", "nodes");
        if (id is null || comments.Count == 0) return null;

        var first = comments[0];
        var at = Js.Time(first, "createdAt");
        if (at is null) return null;
        var (author, bot) = Who(first, "author");

        var replies = new List<ThreadReply>();
        for (var i = 1; i < comments.Count; i++)
        {
            var when = Js.Time(comments[i], "createdAt");
            if (when is null) continue;
            var (replyAuthor, _) = Who(comments[i], "author");
            replies.Add(new ThreadReply(replyAuthor, when.Value, ConversationBuilder.CleanBody(Js.Str(comments[i], "body"))));
        }

        return new ConversationItem("thread-" + id, ConversationKind.Thread, author, bot, at.Value,
            ConversationBuilder.CleanBody(Js.Str(first, "body")), PipelineJson.SafeUrl(Js.Str(first, "url")))
        {
            Resolved = Js.Bool(node, "isResolved"),
            Outdated = Js.Bool(node, "isOutdated"),
            Path = Js.Str(node, "path"),
            Line = Js.Int(node, "line"),
            Replies = replies,
        };
    }

    private static ConversationItem? EventOf(JsonElement node)
    {
        var type = Js.Str(node, "__typename");
        var id = Js.Str(node, "id");
        var at = Js.Time(node, "createdAt");
        if (type is null || id is null || at is null) return null;

        var (actor, bot) = Who(node, "actor");
        string? text = type switch
        {
            "LabeledEvent" => "added the label " + (Js.Str(node, "label", "name") ?? "(unknown)"),
            "ReadyForReviewEvent" => "marked this ready for review",
            "ConvertToDraftEvent" => "converted this to a draft",
            "ReviewRequestedEvent" => "requested a review from " + (Js.Str(node, "requestedReviewer", "login") ?? Js.Str(node, "requestedReviewer", "name") ?? "someone"),
            "MergedEvent" => "merged this pull request",
            "ClosedEvent" => "closed this pull request",
            "ReopenedEvent" => "reopened this pull request",
            "HeadRefForcePushedEvent" => "force-pushed the branch",
            _ => null,
        };
        if (text is null) return null;

        return new ConversationItem("event-" + id, ConversationKind.Event, actor, bot, at.Value, string.Empty, string.Empty)
        {
            Title = ConversationBuilder.CleanBody(text),
        };
    }

    private static ReviewStateKind? ReviewStateOf(string? state) => state switch
    {
        "APPROVED" => ReviewStateKind.Approved,
        "CHANGES_REQUESTED" => ReviewStateKind.ChangesRequested,
        "COMMENTED" => ReviewStateKind.Commented,
        "DISMISSED" => ReviewStateKind.Dismissed,
        _ => null,
    };

    private static (string Login, bool IsBot) Who(JsonElement node, string field)
    {
        var login = Js.Str(node, field, "login");
        if (string.IsNullOrWhiteSpace(login)) return ("ghost", false);
        var bot = Js.Str(node, field, "__typename") == "Bot" || IsBotLogin(login);
        return (login, bot);
    }

    private static bool IsBotLogin(string login) =>
        login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) || login.EndsWith("-bot", StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var line = text.Split('\n')[0];
        var cleaned = ConversationBuilder.CleanBody(line);
        return cleaned.Length > 140 ? cleaned.Substring(0, 140) + "…" : cleaned;
    }
}
