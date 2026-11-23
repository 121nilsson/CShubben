namespace Cshubben.Core.Features.Claude;

/// <summary>
/// The things the app asks Claude to do. Each can have its own model and effort in settings, so cheap summaries do not
/// have to run on the same model as a careful review.
/// </summary>
public enum ClaudeFeature
{
    /// <summary>The short briefing on the Overview page.</summary>
    Briefing,

    /// <summary>Summary, explanation and questions about one pull request.</summary>
    PullRequest,

    /// <summary>A full review draft.</summary>
    Review,

    /// <summary>A question about one or more lines of code.</summary>
    CodeQuestion,

    /// <summary>What happened in the repositories: yesterday, this week, this year.</summary>
    Activity,

    /// <summary>Drafted replies and Linear issues.</summary>
    Drafts,

    /// <summary>Why an error in the logs happened, from the log line and the deployed code.</summary>
    ErrorAnalysis,
}

public static class ClaudeFeatures
{
    public static readonly IReadOnlyList<ClaudeFeature> All = Enum.GetValues<ClaudeFeature>();

    public static string Label(ClaudeFeature feature) => feature switch
    {
        ClaudeFeature.Briefing => "Briefing on Overview",
        ClaudeFeature.PullRequest => "Pull request summary and questions",
        ClaudeFeature.Review => "Review drafts",
        ClaudeFeature.CodeQuestion => "Questions about code lines",
        ClaudeFeature.Activity => "Activity summaries (day, week, year)",
        ClaudeFeature.ErrorAnalysis => "Error analysis on Errors",
        _ => "Drafted replies and Linear issues",
    };

    /// <summary>The model a feature uses when nothing is chosen for it: summaries of what happened are cheap, the rest follow the general setting.</summary>
    public static string DefaultModel(ClaudeFeature feature) => feature == ClaudeFeature.Activity ? "haiku" : "";

    /// <summary>The effort a feature uses when nothing is chosen for it.</summary>
    public static string DefaultEffort(ClaudeFeature feature) => feature == ClaudeFeature.Activity ? "low" : "";

    /// <summary>Reads a feature name saved in settings. Null for anything unknown.</summary>
    public static ClaudeFeature? Parse(string? name) =>
        Enum.TryParse<ClaudeFeature>(name, ignoreCase: true, out var f) && Enum.IsDefined(f) ? f : null;
}
