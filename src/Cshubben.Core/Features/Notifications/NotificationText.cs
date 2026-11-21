using System.Globalization;

namespace Cshubben.Core.Features.Notifications;

/// <summary>Turns events into the words on a toast.</summary>
public static class NotificationText
{
    public const int MaxDigestLines = 4;

    public static string Headline(NotificationEvent e)
    {
        var what = e.Repo + " #" + e.Number.ToString(CultureInfo.InvariantCulture);
        return e.Kind switch
        {
            NotificationKind.CiFailed => "CI failed: " + what,
            NotificationKind.ChangesRequested => "Changes requested: " + what,
            NotificationKind.ReReviewRequested => "Re-review requested: " + what,
            NotificationKind.ReviewRequested => "Review requested: " + what,
            NotificationKind.Mention => "You were mentioned: " + what,
            NotificationKind.ReadyToMerge => "Ready to merge: " + what,
            NotificationKind.Approved => "Approved: " + what,
            NotificationKind.NewInImportantRepo => "New pull request: " + what,
            NotificationKind.Comment => "New comment: " + what,
            NotificationKind.Merged => "Merged: " + what,
            _ => what,
        };
    }

    public static string Body(NotificationEvent e)
    {
        var detail = e.Detail?.Trim() ?? "";
        return e.Kind switch
        {
            NotificationKind.ReviewRequested or NotificationKind.ReReviewRequested =>
                detail.Length > 0 ? detail + " - " + e.Title : e.Title,
            NotificationKind.Mention => detail.Length > 0 ? detail + " mentioned you - " + e.Title : e.Title,
            NotificationKind.Comment or NotificationKind.NewInImportantRepo => detail.Length > 0 ? detail + " - " + e.Title : e.Title,
            _ => e.Title,
        };
    }

    public static ToastMessage Single(NotificationEvent e) =>
        new(Headline(e), Body(e), string.IsNullOrWhiteSpace(e.Url) ? null : e.Url, false, 1, e.Kind);

    /// <summary>One toast for many events: a count, the most important headlines, and "and N more".</summary>
    public static ToastMessage Digest(IReadOnlyList<NotificationEvent> events)
    {
        var n = events.Count;
        var lines = events.Take(MaxDigestLines).Select(Headline).ToList();
        if (n > MaxDigestLines) lines.Add("and " + (n - MaxDigestLines).ToString(CultureInfo.InvariantCulture) + " more");
        return new ToastMessage(
            n.ToString(CultureInfo.InvariantCulture) + " pull request updates",
            string.Join("\n", lines),
            null,
            true,
            n,
            null);
    }
}
