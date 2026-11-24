using System.Globalization;
using System.Text;

namespace Cshubben.Core.Features.Errors;

/// <summary>Web addresses for things the Errors page links to.</summary>
public static class ErrorLinks
{
    /// <summary>
    /// The source file of a stack frame on GitHub, at the deployed commit when it is known and at the default branch (HEAD) when not.
    /// Build paths in the logs lose their leading "src/", so it is put back. Null without a repository or a file.
    /// </summary>
    public static string? FileUrl(string? repo, string? file, int? line, string? sha = null)
    {
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(file)) return null;
        var reference = string.IsNullOrWhiteSpace(sha) ? "HEAD" : sha.Trim();
        var anchor = line is { } l && l > 0 ? "#L" + l.ToString(CultureInfo.InvariantCulture) : "";
        return "https://github.com/" + repo.Trim() + "/blob/" + reference + "/src/" + file.Trim().TrimStart('/') + anchor;
    }

    /// <summary>A commit on GitHub. Null without a repository or a commit.</summary>
    public static string? CommitUrl(string? repo, string? sha) =>
        string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(sha)
            ? null
            : "https://github.com/" + repo.Trim() + "/commit/" + sha.Trim();
}

/// <summary>
/// Plain-text summaries of an error spike or one error, in Markdown, for pasting into a chat, a ticket or a pull request.
/// Everything that comes from the logs passes through <see cref="LogRedactor"/>, so nothing sensitive is copied by accident.
/// Pure, so the wording is tested.
/// </summary>
public static class ErrorReport
{
    private const int MaxGroups = 5;
    private const int MaxLine = 200;

    /// <summary>The summary of one application: how bad it is, since when, after which deployment, and its top errors.</summary>
    public static string App(
        AppSpike app,
        ErrorWindow window,
        IReadOnlyList<ErrorGroup> groups,
        DateTimeOffset? onset,
        DeployLink? link,
        string? repo,
        DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(app.App).Append(": ")
            .Append(app.IsSpike ? "errors spiking " + ErrorsText.Factor(app.Factor) : app.Total == 0 ? "no errors" : "errors")
            .Append('\n');

        if (app.Total > 0)
        {
            sb.Append("- ").Append(ErrorsText.PerHour(app, window)).Append(" now, ")
                .Append(app.Total.ToString(CultureInfo.InvariantCulture)).Append(app.Total == 1 ? " error" : " errors")
                .Append(" in the last 24 hours, ").Append(ErrorsText.TrendWord(app.Trend)).Append('\n');
        }

        if (onset is { } started)
        {
            sb.Append("- ").Append(ErrorsText.StartLine(started, now, link)).Append('\n');
        }

        if (link is not null && repo is not null && ErrorLinks.CommitUrl(repo, link.Deploy.Sha) is { } commit)
            sb.Append("- Deployed commit: ").Append(commit).Append('\n');
        if (repo is not null) sb.Append("- Repository: ").Append(repo).Append('\n');

        var top = groups.Take(MaxGroups).ToList();
        if (top.Count > 0)
        {
            sb.Append("\nTop errors:\n");
            for (var i = 0; i < top.Count; i++)
            {
                var g = top[i];
                sb.Append(i + 1).Append(". ").Append(Clean(g.Title)).Append(": ")
                    .Append(g.Count.ToString(CultureInfo.InvariantCulture)).Append(g.Count == 1 ? " error" : " errors")
                    .Append(", first ").Append(ErrorsText.Ago(g.FirstSeen, now))
                    .Append(", last ").Append(ErrorsText.Ago(g.LastSeen, now)).Append('\n');
            }

            if (groups.Count > top.Count)
                sb.Append("(and ").Append((groups.Count - top.Count).ToString(CultureInfo.InvariantCulture)).Append(" more)\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The summary of one error group: what, how often, where in the code, and after which deployment it appeared.</summary>
    public static string Group(
        string app,
        ErrorGroup group,
        GroupStatus status,
        string? repo,
        DeployedVersion? running,
        DeployLink? firstSeenAfter,
        DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.Append("**").Append(Clean(group.Title)).Append("** in ").Append(app).Append('\n');
        sb.Append("- ").Append(group.Count.ToString(CultureInfo.InvariantCulture)).Append(group.Count == 1 ? " error" : " errors")
            .Append(" in 24 hours, first ").Append(ErrorsText.Ago(group.FirstSeen, now))
            .Append(", last ").Append(ErrorsText.Ago(group.LastSeen, now));
        if (group.Pods.Count > 0)
            sb.Append(", on ").Append(group.Pods.Count.ToString(CultureInfo.InvariantCulture)).Append(group.Pods.Count == 1 ? " pod" : " pods");
        sb.Append('\n');

        if (status is GroupStatus.New or GroupStatus.Rising or GroupStatus.Fading or GroupStatus.Stopped)
            sb.Append("- Status: ").Append(GroupStatuses.Label(status).ToLowerInvariant()).Append('\n');

        if (firstSeenAfter is not null) sb.Append("- ").Append(ErrorsText.DeployLine("First seen", firstSeenAfter)).Append('\n');

        if (group.Method is not null)
        {
            sb.Append("- Code: `").Append(group.Method).Append('`');
            if (ErrorLinks.FileUrl(repo, group.File, group.Line, running?.Sha) is { } url && group.File is not null)
            {
                var where = group.File + (group.Line is { } l ? ":" + l.ToString(CultureInfo.InvariantCulture) : "");
                sb.Append(" at [").Append(where).Append("](").Append(url).Append(')');
            }

            sb.Append('\n');
        }

        var latest = group.Samples is { Count: > 0 } samples ? samples[0] : null;
        if (latest is not null)
        {
            var first = FirstMessageLine(latest.Message);
            if (first.Length > 0) sb.Append("> ").Append(first).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The first line of the message of a log line, not the header, redacted and cut.</summary>
    private static string FirstMessageLine(string message)
    {
        var lines = message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = lines.Length > 0 && lines[0].TrimStart().StartsWith('[') ? 1 : 0;
        for (var i = index; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            line = LogRedactor.Redact(line);
            return line.Length <= MaxLine ? line : line[..(MaxLine - 1)] + "…";
        }

        return "";
    }

    private static string Clean(string text) => LogRedactor.Redact(text).Replace("\n", " ").Trim();
}
