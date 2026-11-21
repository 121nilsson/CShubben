using Cshubben.Core.Features.Claude;
using Cshubben.Core.Model;

namespace Cshubben.Ui.Assist;

enum ClaudePreset { Summarize, DraftReview, Explain, Free }

/// <summary>
/// Turns a preset into a prompt. Summaries and review drafts use the shared prompts, which keep all GitHub text inside the DATA
/// section. Explain and free questions reuse the summary prompt (same untrusted-data rules) and add the user's own, trusted request
/// after it.
/// </summary>
static class ClaudePresets
{
    public const string QuestionMarker = "User question (from the app user, trusted): ";

    public const string ExplainQuestion =
        "Explain this change step by step for a reviewer who has not seen the code: what problem it solves, how the pieces fit together, "
        + "and what to double-check. Stay within what the data shows.";

    public static string Label(ClaudePreset preset) => preset switch
    {
        ClaudePreset.Summarize => "Summarize PR",
        ClaudePreset.DraftReview => "Draft review",
        ClaudePreset.Explain => "Explain",
        _ => "Question",
    };

    public static string Hint(ClaudePreset preset) => preset switch
    {
        ClaudePreset.Summarize => "A short summary with risk areas",
        ClaudePreset.DraftReview => "A draft code review you can edit. Nothing is posted.",
        ClaudePreset.Explain => "Walk through the change step by step",
        _ => "Ask anything about this pull request",
    };

    public static ClaudeRequest Build(ClaudePreset preset, PullRequest pr, string diff, string question)
    {
        switch (preset)
        {
            case ClaudePreset.Summarize:
                return ClaudeRequest.From(Prompts.PrSummary(pr, diff)) with { Feature = ClaudeFeature.PullRequest };
            case ClaudePreset.DraftReview:
                return ClaudeRequest.From(Prompts.ReviewDraft(pr, diff)) with { Timeout = ClaudeRequest.ReviewTimeout, Feature = ClaudeFeature.Review };
            case ClaudePreset.Explain:
                return WithQuestion(pr, diff, ExplainQuestion);
            default:
                return WithQuestion(pr, diff, question);
        }
    }

    private static ClaudeRequest WithQuestion(PullRequest pr, string diff, string question)
    {
        var basePrompt = Prompts.PrSummary(pr, diff);
        var system = basePrompt.SystemPrompt
            + "\n- The app user's own request is at the end of the user message, after the data. Answer that request instead of writing a summary. "
            + "Keep every rule above, answer in Markdown and keep it concise.";
        var user = basePrompt.UserPrompt + "\n\n" + QuestionMarker + PromptText.OneLine(question, 1_000);
        return ClaudeRequest.From(new PromptPair(system, user)) with { Feature = ClaudeFeature.PullRequest };
    }
}
