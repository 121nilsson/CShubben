namespace Cshubben.Core.Features.Errors;

/// <summary>What a Linear issue for an error group will say. Priority uses Linear's scale: 1 urgent, 2 high, 3 medium, 4 low.</summary>
public sealed record ErrorIssueDraft(string Title, string Description, int Priority);

/// <summary>
/// Drafts a Linear issue from an error group. The description is the redacted group summary, so nothing sensitive reaches Linear.
/// Pure, so the rules are tested.
/// </summary>
public static class ErrorIssue
{
    private const int MaxTitle = 120;

    /// <summary>Errors in 24 hours from which a new or rising error counts as high priority.</summary>
    public const int HighCount = 20;

    public static ErrorIssueDraft Draft(
        string app,
        ErrorGroup group,
        GroupStatus status,
        string? repo,
        DeployedVersion? running,
        DeployLink? firstSeenAfter,
        DateTimeOffset now)
    {
        var title = app + ": " + LogRedactor.Redact(group.Title).Replace("\r", " ").Replace("\n", " ").Trim();
        if (title.Length > MaxTitle) title = title[..(MaxTitle - 1)] + "…";

        var description = ErrorReport.Group(app, group, status, repo, running, firstSeenAfter, now)
            + "\n\nCreated from the Errors page in Cshubben.";
        return new ErrorIssueDraft(title, description, PriorityOf(group, status));
    }

    /// <summary>New or rising errors with many occurrences are high, other live errors medium, fading or stopped ones low.</summary>
    public static int PriorityOf(ErrorGroup group, GroupStatus status) => status switch
    {
        GroupStatus.Fading or GroupStatus.Stopped => 4,
        GroupStatus.New or GroupStatus.Rising when group.Count >= HighCount => 2,
        _ => 3,
    };
}
