using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// A stand-in for Claude that answers from canned text, so demo mode and screenshots work without the claude CLI.
/// It waits a moment first so the running state is visible.
/// </summary>
public sealed class DemoClaudeData : IClaudeAssistant
{
    public bool IsDemo => true;

    public bool IsAvailable => true;

    public async Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(900, cancellationToken);
        var user = request.UserPrompt;
        var title = Field(user, "Title") ?? "this pull request";
        var repo = Field(user, "Repo") ?? "the repository";
        var number = Field(user, "Number") ?? "#0";

        string text;
        if (user.Contains("Draft a Linear issue", StringComparison.Ordinal))
        {
            text = "{\"title\": " + Json(Shorten(title, 80)) + ", \"description\": " +
                   Json("## Context\nTracks the work in " + repo + " " + number + ".\n\n## Done when\n- the change is merged\n- follow-up tasks are listed") +
                   ", \"priority\": 3, \"labels\": [\"feature\"]}";
        }
        else if (user.Contains("draft code review", StringComparison.Ordinal))
        {
            text = "## Summary\nThe change behind **" + title + "** is focused and easy to follow.\n\n" +
                   "## Risks and bugs\n- `src/payments/Handler.cs`: the new key lookup is not covered by a test for concurrent requests (unsure, cannot tell from the diff).\n" +
                   "- Error handling swallows the timeout case.\n\n" +
                   "## Suggestions\n- Add a test for two requests with the same key.\n- Log the key with the request id, not the payload.\n\n" +
                   "## Questions for the author\n- Is the 24 hour key lifetime documented anywhere?";
        }
        else if (user.Contains("Summarize what was merged", StringComparison.Ordinal))
        {
            text = "Demo summary: the team merged a steady stream of small changes, mostly in **acme/api** and **acme/web**.\n\n" +
                   "- **acme/api**: payment retries and idempotency keys landed.\n- **acme/web**: the settings page was reworked.\n\n" +
                   "Nothing was sent to Claude in demo mode.";
        }
        else if (user.Contains("User question (from the app user", StringComparison.Ordinal))
        {
            var question = FieldAfter(user, "User question (from the app user, trusted): ") ?? "your question";
            text = "Here is a demo answer to: *" + Shorten(question, 120) + "*\n\n" +
                   "In the real app Claude reads the pull request metadata, description and diff and answers in Markdown. " +
                   "Nothing was sent to Claude in demo mode.";
        }

        else
        {
            text = "**" + title + "** (" + repo + " " + number + ")\n\n" +
                   "- Adds the core change and wires it into the existing request path.\n" +
                   "- Touches tests for the happy path; error paths look thinner.\n" +
                   "- Risk areas: concurrency and retries.\n\n" +
                   "_This is a demo answer. Nothing was sent to Claude._";
        }

        return new ClaudeResult(true, text, "sonnet", 0.0187, TimeSpan.FromSeconds(6.3), ClaudeErrorKind.None, null, 4210, 520);
    }

    /// <summary>The briefing shown in demo mode, built from the snapshot so the links point at real demo PRs.</summary>
    public static BriefingResult Briefing(HubSnapshot snapshot)
    {
        var overview = OverviewModel.Compute(snapshot);
        var items = new List<BriefingItem>();

        foreach (var a in overview.ReadyToMerge.Take(2))
            items.Add(new BriefingItem(BriefingItemKind.Action, $"Merge {a.Pr.Repo} #{a.Pr.Number}: {Shorten(a.Pr.Title, 70)}. Approved, green, no conflicts.", a.Pr.Url));

        foreach (var a in overview.NeedsAction.Take(3))
        {
            var kind = a.Action.Tone == Tone.Danger ? BriefingItemKind.Action : BriefingItemKind.Watch;
            items.Add(new BriefingItem(kind, $"{a.Action.Label} on {a.Pr.Repo} #{a.Pr.Number}. {a.Action.Reason}", a.Pr.Url));
        }

        foreach (var a in overview.ReviewsForYou.Take(2))
            items.Add(new BriefingItem(BriefingItemKind.Action, $"{a.Action.Label}: {a.Pr.Repo} #{a.Pr.Number} from {a.Pr.Author}, {Shorten(a.Pr.Title, 60)}.", a.Pr.Url));

        if (overview.Waiting > 0)
            items.Add(new BriefingItem(BriefingItemKind.Info, $"{overview.Waiting} of your pull requests are waiting on others. Nothing to do there.", null));

        var headline = overview.NeedsYou == 0
            ? "All clear today"
            : $"{overview.NeedsYou} things need you, {overview.ReadyToMerge.Count} ready to merge";
        var summary = "Start with the approved pull requests: they are green and can ship now. "
            + "Then unblock the failing and conflicting ones, and give the review requests a pass before they go stale. "
            + "This is a demo briefing built from demo data.";
        return new BriefingResult(headline, summary, items.Take(BriefingParser.MaxItems).ToList());
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>Reads a "| Label: value" line from the DATA section of a prompt.</summary>
    private static string? Field(string prompt, string label) => FieldAfter(prompt, "| " + label + ": ");

    private static string? FieldAfter(string prompt, string marker)
    {
        var start = prompt.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = prompt.IndexOf('\n', start);
        var value = end < 0 ? prompt.Substring(start) : prompt.Substring(start, end - start);
        return value.Trim();
    }
}

/// <summary>Demo actions: nothing is ever sent. The answers say so.</summary>
public sealed class DemoInspectorActions : IInspectorActions
{
    public bool IsDryRun => true;

    public async Task<ActionOutcome> SubmitReviewAsync(PullRequest pr, ReviewVerdict verdict, string body, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
        var what = verdict == ReviewVerdict.Approve ? "approve" : "request changes on";
        return new ActionOutcome(true, true, $"Demo mode: nothing was sent. In the real app this would {what} {pr.Repo} #{pr.Number}.");
    }

    public Task<string> GetDiffAsync(PullRequest pr, CancellationToken cancellationToken) =>
        Task.FromResult(
            "diff --git a/src/Handler.cs b/src/Handler.cs\n--- a/src/Handler.cs\n+++ b/src/Handler.cs\n@@ -10,6 +10,12 @@ class Handler\n" +
            " {\n-    var result = Process(request);\n+    var key = request.Headers[\"Idempotency-Key\"];\n+    if (store.TryGet(key, out var cached)) return cached;\n+    var result = Process(request);\n+    store.Set(key, result, TimeSpan.FromHours(24));\n     return result;\n }\n");
}
