using System.Globalization;

namespace Cshubben.Core.Features.Notifications;

/// <summary>A deployment that failed, as the background Drift check saw it.</summary>
/// <param name="Environment">Environment name.</param>
/// <param name="Key">Stable id of that deployment (its id), so the same failure is reported once.</param>
public sealed record WatchedDeployFailure(string Environment, string Key);

/// <summary>What the background Drift check saw in one repository. Plain values: the composition root fills it.</summary>
/// <param name="Repo">owner/name.</param>
/// <param name="MainRed">The default branch has a failing workflow.</param>
/// <param name="RunKey">Id of the failing run(s), so a new failure is told apart from an old one.</param>
/// <param name="MainDetail">One line for the toast, for example "main is red: CI".</param>
/// <param name="DeployFailures">Recent notable deployment failures.</param>
public sealed record WatchedRepoState(
    string Repo,
    bool MainRed,
    string RunKey,
    string MainDetail,
    IReadOnlyList<WatchedDeployFailure> DeployFailures);

/// <summary>An application whose errors are spiking, as the background Errors check saw it.</summary>
/// <param name="App">Application name.</param>
/// <param name="Key">Identifies this spike (the application and when it began), so the same spike is reported once.</param>
/// <param name="Text">One line for the toast, for example "181 / h now, 7.5× the usual rate".</param>
public sealed record WatchedErrorSpike(string App, string Key, string Text);

public sealed record WatchedIssue(string Identifier, string Title, string Url);

/// <summary>A pull request and Linear issue that disagree.</summary>
/// <param name="Key">Stable key: kind, issue and pull request.</param>
public sealed record WatchedOutOfSync(string Key, string Identifier, string Text, string Url);

/// <summary>What the background Linear check saw: my open issues and the out-of-sync items worth a toast.</summary>
public sealed record WatchedLinear(IReadOnlyList<WatchedIssue> Issues, IReadOnlyList<WatchedOutOfSync> OutOfSync);

/// <summary>
/// Finds what changed between two background checks and turns it into notification events.
/// Pure. The first check (previous is null) is only a baseline. Something that is new to the check
/// (a repo just watched, an issue just loaded) is never reported as a change.
/// </summary>
public static class WatchDiff
{
    public static IReadOnlyList<NotificationEvent> Drift(IReadOnlyList<WatchedRepoState>? previous, IReadOnlyList<WatchedRepoState> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null) return Array.Empty<NotificationEvent>();

        var old = new Dictionary<string, WatchedRepoState>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in previous) old.TryAdd(o.Repo, o);

        var events = new List<NotificationEvent>();
        foreach (var now in current)
        {
            if (!old.TryGetValue(now.Repo, out var before)) continue;
            var url = "https://github.com/" + now.Repo + "/actions";
            var repoKey = now.Repo.ToLowerInvariant();

            if (now.MainRed && !before.MainRed)
                events.Add(new NotificationEvent("main-red:" + repoKey + ":" + now.RunKey, NotificationKind.MainRed, now.Repo, now.Repo, 0, now.MainDetail, url));

            foreach (var failure in now.DeployFailures)
            {
                if (before.DeployFailures.Any(f => f.Key == failure.Key)) continue;
                events.Add(new NotificationEvent(
                    "deploy-failed:" + repoKey + ":" + failure.Key, NotificationKind.DeployFailed, now.Repo, now.Repo, 0,
                    "Deployment to " + failure.Environment + " failed", url, failure.Environment));
            }
        }

        return events;
    }

    /// <summary>Address that a toast carries to open the Errors page on one application.</summary>
    public const string ErrorsScheme = "cshubben://errors/";

    /// <summary>An application that spikes now and did not at the previous check. A spike that goes on is reported once.</summary>
    public static IReadOnlyList<NotificationEvent> Errors(IReadOnlyList<WatchedErrorSpike>? previous, IReadOnlyList<WatchedErrorSpike> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null) return Array.Empty<NotificationEvent>();

        var known = new HashSet<string>(previous.Select(s => s.App), StringComparer.OrdinalIgnoreCase);
        var events = new List<NotificationEvent>();
        foreach (var spike in current)
        {
            if (known.Contains(spike.App)) continue;
            events.Add(new NotificationEvent(
                "error-spike:" + spike.Key, NotificationKind.ErrorSpike, spike.App, "", 0, spike.Text,
                ErrorsScheme + Uri.EscapeDataString(spike.App), spike.App));
        }

        return events;
    }

    /// <summary>The application in a toast address made by <see cref="Errors"/>, or null when it is not one.</summary>
    public static string? AppFromErrorsUrl(string? url) =>
        url is not null && url.StartsWith(ErrorsScheme, StringComparison.Ordinal) && url.Length > ErrorsScheme.Length
            ? Uri.UnescapeDataString(url[ErrorsScheme.Length..])
            : null;

    public static IReadOnlyList<NotificationEvent> Linear(WatchedLinear? previous, WatchedLinear current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null) return Array.Empty<NotificationEvent>();

        var events = new List<NotificationEvent>();
        var knownIssues = new HashSet<string>(previous.Issues.Select(i => i.Identifier), StringComparer.OrdinalIgnoreCase);
        foreach (var issue in current.Issues)
        {
            if (knownIssues.Contains(issue.Identifier)) continue;
            events.Add(new NotificationEvent(
                "linear-assigned:" + issue.Identifier.ToLowerInvariant(), NotificationKind.LinearAssigned,
                issue.Identifier, "", 0, issue.Title, issue.Url, issue.Identifier));
        }

        var knownSync = new HashSet<string>(previous.OutOfSync.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var item in current.OutOfSync)
        {
            if (knownSync.Contains(item.Key)) continue;
            events.Add(new NotificationEvent(
                "linear-sync:" + item.Key.ToLowerInvariant(), NotificationKind.LinearOutOfSync,
                item.Identifier, "", 0, item.Text, item.Url, item.Identifier));
        }

        return events;
    }

    /// <summary>Number as a plain string, for composing keys.</summary>
    public static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}
