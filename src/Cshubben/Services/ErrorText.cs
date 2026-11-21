using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// Turns exceptions into calm, specific text. The classification itself is pure and tested in Core
/// (<see cref="FailureClassifier"/>); this class only supplies the feature facts and the current time.
/// Never includes tokens (our exceptions do not carry them, and the classifier scrubs anyway).
/// </summary>
static class ErrorText
{
    /// <summary>Classifies an exception. <paramref name="service"/> defaults to GitHub, or Linear for Linear exceptions.</summary>
    public static Failure Classify(Exception ex, string? service = null)
    {
        var resetAt = AppServices.Current.GitHub?.Rest.LastRateLimit?.ResetAt;
        var facts = GitHubFailureFacts.From(ex, resetAt) ?? LinearFailureFacts.From(ex);
        var name = service ?? (ex is LinearException ? "Linear" : "GitHub");
        return FailureClassifier.Classify(ex, name, DateTimeOffset.UtcNow, facts);
    }

    /// <summary>Facts for the diagnostics runner, which classifies per check.</summary>
    public static FailureFacts? FactsFor(Exception ex) =>
        GitHubFailureFacts.From(ex, AppServices.Current.GitHub?.Rest.LastRateLimit?.ResetAt) ?? LinearFailureFacts.From(ex);

    /// <summary>One short sentence or two: what happened and what to do.</summary>
    public static string Describe(Exception ex) => Classify(ex).Message;
}
