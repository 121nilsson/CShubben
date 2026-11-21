namespace Cshubben.Core.Features.Notifications;

/// <summary>
/// What happened. The declaration order is the priority order: earlier kinds are shown first and
/// are the ones that make it into a digest summary.
/// </summary>
public enum NotificationKind
{
    CiFailed,
    ChangesRequested,
    ReReviewRequested,
    ReviewRequested,
    Mention,
    ReadyToMerge,
    Approved,
    NewInImportantRepo,
    Comment,
    Merged,
}

/// <summary>One thing worth telling the user about, found by diffing two snapshots.</summary>
/// <param name="Key">Stable key used for de-duplication through the persisted seen-set.</param>
/// <param name="Kind">What happened.</param>
/// <param name="PrId">Id of the pull request (or "owner/repo#n" when it is not in the snapshot).</param>
/// <param name="Repo">owner/name.</param>
/// <param name="Number">Pull request number.</param>
/// <param name="Title">Pull request title.</param>
/// <param name="Url">Link to the pull request.</param>
/// <param name="Detail">Short extra text (author, comment count, ...). May be empty.</param>
public sealed record NotificationEvent(
    string Key,
    NotificationKind Kind,
    string PrId,
    string Repo,
    int Number,
    string Title,
    string Url,
    string Detail = "");

/// <summary>A toast ready to be shown by the platform notifier.</summary>
/// <param name="Title">First line.</param>
/// <param name="Body">Second line(s).</param>
/// <param name="Url">What a click opens; null opens the app only.</param>
/// <param name="IsDigest">True when this message summarises several events.</param>
/// <param name="Count">How many events the message stands for.</param>
/// <param name="Kind">The kind for a single-event message; null for a digest.</param>
public sealed record ToastMessage(string Title, string Body, string? Url, bool IsDigest, int Count, NotificationKind? Kind);

/// <summary>What <see cref="NotificationEngine.Plan"/> decided.</summary>
/// <param name="Messages">Toasts to show now, in order.</param>
/// <param name="Delivered">The events those toasts stand for.</param>
/// <param name="Deferred">Events held back by quiet hours (shown when quiet hours end, while the app keeps running).</param>
/// <param name="Dropped">Events removed by toggles, muted repos or the seen-set.</param>
public sealed record NotificationPlan(
    IReadOnlyList<ToastMessage> Messages,
    IReadOnlyList<NotificationEvent> Delivered,
    int Deferred,
    int Dropped)
{
    public static NotificationPlan Empty { get; } =
        new(Array.Empty<ToastMessage>(), Array.Empty<NotificationEvent>(), 0, 0);
}

/// <summary>
/// Everything that decides whether and how a notification is shown. Plain values only (no reference to the
/// settings feature): the composition root fills it from the app settings.
/// </summary>
public sealed record NotificationPrefs
{
    public const int DefaultMaxIndividual = 3;

    /// <summary>Master switch.</summary>
    public bool Enabled { get; init; } = true;

    public bool ReviewRequested { get; init; } = true;

    public bool ReReviewRequested { get; init; } = true;

    public bool CiFailed { get; init; } = true;

    public bool ChangesRequested { get; init; } = true;

    public bool ReadyToMerge { get; init; } = true;

    public bool Comment { get; init; } = true;

    public bool Mention { get; init; } = true;

    /// <summary>My pull request was approved (but is not ready to merge yet; that has its own message).</summary>
    public bool Approved { get; init; } = true;

    /// <summary>Someone else opened a pull request in a repo marked important.</summary>
    public bool NewInImportantRepo { get; init; } = true;

    /// <summary>Repos (owner/name) marked important. Only these produce <see cref="NotificationKind.NewInImportantRepo"/>.</summary>
    public IReadOnlyList<string> ImportantRepos { get; init; } = Array.Empty<string>();

    /// <summary>Off by default: silence means fine.</summary>
    public bool Merged { get; init; }

    public bool QuietHoursEnabled { get; init; }

    /// <summary>Quiet hours start, minutes after local midnight (default 22:00).</summary>
    public int QuietStartMinute { get; init; } = 22 * 60;

    /// <summary>Quiet hours end, minutes after local midnight (default 07:00). May be earlier than the start.</summary>
    public int QuietEndMinute { get; init; } = 7 * 60;

    /// <summary>Repos (owner/name) that never notify.</summary>
    public IReadOnlyList<string> MutedRepos { get; init; } = Array.Empty<string>();

    /// <summary>Owners or organisations that never notify.</summary>
    public IReadOnlyList<string> MutedOrgs { get; init; } = Array.Empty<string>();

    /// <summary>Closing the window hides it in the tray instead of quitting.</summary>
    public bool CloseToTray { get; init; }

    /// <summary>More events than this at once become one digest toast.</summary>
    public int MaxIndividual { get; init; } = DefaultMaxIndividual;

    public bool IsKindEnabled(NotificationKind kind) => kind switch
    {
        NotificationKind.CiFailed => CiFailed,
        NotificationKind.ChangesRequested => ChangesRequested,
        NotificationKind.ReReviewRequested => ReReviewRequested,
        NotificationKind.ReviewRequested => ReviewRequested,
        NotificationKind.Mention => Mention,
        NotificationKind.ReadyToMerge => ReadyToMerge,
        NotificationKind.Approved => Approved,
        NotificationKind.NewInImportantRepo => NewInImportantRepo,
        NotificationKind.Comment => Comment,
        NotificationKind.Merged => Merged,
        _ => false,
    };

    /// <summary>True when the repo is marked important. Names compare case-insensitively.</summary>
    public bool IsImportant(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return false;
        var name = repo.Trim();
        foreach (var r in ImportantRepos ?? Array.Empty<string>())
            if (string.Equals(r?.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True when the repo, or its owner, is muted. Names compare case-insensitively.</summary>
    public bool IsMuted(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return false;
        var name = repo.Trim();
        var slash = name.IndexOf('/');
        var owner = slash > 0 ? name.Substring(0, slash) : name;
        foreach (var m in MutedRepos ?? Array.Empty<string>())
            if (string.Equals(m?.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var o in MutedOrgs ?? Array.Empty<string>())
            if (string.Equals(o?.Trim(), owner, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Clamps values loaded from disk so the rest of the code can trust them.</summary>
    public NotificationPrefs Sanitized() => this with
    {
        QuietStartMinute = Clamp(QuietStartMinute, 0, 1439, 22 * 60),
        QuietEndMinute = Clamp(QuietEndMinute, 0, 1439, 7 * 60),
        MaxIndividual = MaxIndividual < 1 || MaxIndividual > 20 ? DefaultMaxIndividual : MaxIndividual,
        MutedRepos = MutedRepos ?? Array.Empty<string>(),
        MutedOrgs = MutedOrgs ?? Array.Empty<string>(),
        ImportantRepos = ImportantRepos ?? Array.Empty<string>(),
    };

    private static int Clamp(int value, int min, int max, int fallback) => value < min || value > max ? fallback : value;
}
