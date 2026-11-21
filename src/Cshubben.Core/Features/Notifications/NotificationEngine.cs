using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Notifications;

/// <summary>
/// Decides what to show: diffs two snapshots, applies toggles and the mute list, drops what was already
/// notified (persisted seen-set), holds events back during quiet hours and batches bursts into a digest.
/// Not thread safe: call it from one place (the refresh loop).
/// </summary>
public sealed class NotificationEngine
{
    private const int MaxDeferred = 50;

    private readonly INotificationState _state;
    private readonly SeenSet _seen;
    private readonly List<NotificationEvent> _deferred = new();

    public NotificationEngine(INotificationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _seen = new SeenSet(state.LoadSeen());
    }

    /// <summary>Events waiting for quiet hours to end.</summary>
    public int DeferredCount => _deferred.Count;

    /// <summary>
    /// Plans the toasts for a new snapshot.
    /// </summary>
    /// <param name="previous">The snapshot before; null for the first one (baseline, nothing is shown).</param>
    /// <param name="current">The new snapshot.</param>
    /// <param name="prefs">Toggles, mute list, quiet hours, digest threshold.</param>
    /// <param name="now">The current time. Its offset must be the local offset: quiet hours use its wall-clock time.</param>
    public NotificationPlan Plan(HubSnapshot? previous, HubSnapshot current, NotificationPrefs prefs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(prefs);
        var candidates = new List<NotificationEvent>(_deferred);
        _deferred.Clear();
        candidates.AddRange(NotificationDiff.Compute(previous, current));
        return Decide(candidates, prefs.Sanitized(), now);
    }

    /// <summary>
    /// Plans toasts for events found elsewhere (the background Drift and Linear checks). Same rules as <see cref="Plan"/>:
    /// toggles, mute list, important repos, seen-set, quiet hours and digest.
    /// </summary>
    public NotificationPlan PlanEvents(IEnumerable<NotificationEvent> events, NotificationPrefs prefs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(prefs);
        var candidates = new List<NotificationEvent>(_deferred);
        _deferred.Clear();
        candidates.AddRange(events);
        return Decide(candidates, prefs.Sanitized(), now);
    }

    private NotificationPlan Decide(List<NotificationEvent> candidates, NotificationPrefs p, DateTimeOffset now)
    {
        if (candidates.Count == 0) return NotificationPlan.Empty;

        if (!p.Enabled) return new NotificationPlan(Array.Empty<ToastMessage>(), Array.Empty<NotificationEvent>(), 0, candidates.Count);

        var keep = new List<NotificationEvent>();
        var batchKeys = new HashSet<string>(StringComparer.Ordinal);
        var dropped = 0;
        foreach (var e in candidates)
        {
            if (!p.IsKindEnabled(e.Kind)
                || p.IsMuted(e.Repo)
                || (NotificationKinds.NeedsImportantRepo(e.Kind) && !p.IsImportant(e.Repo))
                || _seen.Contains(e.Key)
                || !batchKeys.Add(e.Key))
            {
                dropped++;
                continue;
            }

            keep.Add(e);
        }

        if (keep.Count == 0) return new NotificationPlan(Array.Empty<ToastMessage>(), Array.Empty<NotificationEvent>(), 0, dropped);

        if (p.QuietHoursEnabled && QuietHours.IsQuiet(p.QuietStartMinute, p.QuietEndMinute, QuietHours.MinuteOfDay(now)))
        {
            _deferred.AddRange(keep.Take(MaxDeferred));
            return new NotificationPlan(Array.Empty<ToastMessage>(), Array.Empty<NotificationEvent>(), _deferred.Count, dropped + Math.Max(0, keep.Count - MaxDeferred));
        }

        foreach (var e in keep) _seen.Add(e.Key, now);
        _seen.Prune(now);
        try
        {
            _state.SaveSeen(_seen.Snapshot());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember is better than not notifying; the worst case is one repeated toast.
        }

        var ordered = keep.OrderBy(e => (int)e.Kind).ToList();
        var messages = new List<ToastMessage>();
        if (ordered.Count <= p.MaxIndividual)
        {
            foreach (var e in ordered) messages.Add(NotificationText.Single(e));
        }
        else
        {
            messages.Add(NotificationText.Digest(ordered));
        }

        return new NotificationPlan(messages, ordered, 0, dropped);
    }
}
