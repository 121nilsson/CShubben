using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>
/// Pure mappers from GitHub REST answers (check runs, commit statuses, workflow runs, deployments, releases, compare)
/// to the pipeline model. Missing or odd values give defaults; invalid entries are skipped. Text is untrusted.
/// </summary>
public static class PipelineJson
{
    private static readonly Regex RunIdPattern = new(@"/actions/runs/(\d{1,18})", RegexOptions.CultureInvariant);

    /// <summary>Maps GitHub's status and conclusion to <see cref="RunState"/>. Unknown conclusions are neutral, never red.</summary>
    public static RunState RunStateOf(string? status, string? conclusion)
    {
        switch (status)
        {
            case "completed":
                switch (conclusion)
                {
                    case "success": return RunState.Success;
                    case "failure":
                    case "timed_out":
                    case "startup_failure": return RunState.Failure;
                    case "cancelled": return RunState.Cancelled;
                    case "action_required": return RunState.Queued;
                    default: return RunState.Skipped;
                }
            case "in_progress": return RunState.Running;
            default: return RunState.Queued;
        }
    }

    /// <summary>Maps a deployment status to <see cref="DeployState"/>. Missing or unknown counts as queued.</summary>
    public static DeployState DeployStateOf(string? state) => state switch
    {
        "success" => DeployState.Success,
        "failure" or "error" => DeployState.Failure,
        "in_progress" => DeployState.InProgress,
        "inactive" => DeployState.Inactive,
        _ => DeployState.Queued,
    };

    /// <summary>The workflow run id inside a GitHub Actions URL such as .../actions/runs/123/job/456, or null.</summary>
    public static long? RunIdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var m = RunIdPattern.Match(url);
        if (!m.Success) return null;
        return long.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }

    /// <summary>Only absolute http(s) links are kept; anything else becomes an empty string.</summary>
    public static string SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return string.Empty;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ? uri.AbsoluteUri : string.Empty;
    }

    /// <summary>True for 7 to 64 hexadecimal characters.</summary>
    public static bool IsSha(string? sha)
    {
        if (string.IsNullOrEmpty(sha) || sha.Length < 7 || sha.Length > 64) return false;
        foreach (var c in sha)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>head.sha of a pull request answer, or null when missing or not a commit id.</summary>
    public static string? HeadShaOf(JsonElement? pullRequest)
    {
        var sha = Js.Str(pullRequest, "head", "sha");
        return IsSha(sha) ? sha : null;
    }

    /// <summary>default_branch of a repository answer, or null.</summary>
    public static string? DefaultBranchOf(JsonElement? repository)
    {
        var branch = Js.Str(repository, "default_branch");
        return string.IsNullOrWhiteSpace(branch) ? null : branch;
    }

    /// <summary>Check runs from <c>GET /commits/{sha}/check-runs</c> (the "check_runs" array).</summary>
    public static IReadOnlyList<CheckRun> ParseCheckRuns(JsonElement? root)
    {
        var result = new List<CheckRun>();
        foreach (var item in Js.Items(root, "check_runs"))
        {
            var id = Js.Long(item, "id");
            var name = CleanName(Js.Str(item, "name"));
            if (id is null || name is null) continue;

            var state = RunStateOf(Js.Str(item, "status"), Js.Str(item, "conclusion"));
            var url = SafeUrl(Js.Str(item, "html_url"));
            var runId = RunIdFromUrl(Js.Str(item, "details_url")) ?? RunIdFromUrl(Js.Str(item, "html_url"));
            string? excerpt = null;
            if (state == RunState.Failure)
            {
                var summary = Js.Str(item, "output", "summary");
                excerpt = LogExcerpt.Clean(string.IsNullOrWhiteSpace(summary) ? Js.Str(item, "output", "title") : summary);
            }

            result.Add(new CheckRun(id.Value, name, state, Js.Time(item, "started_at"), Js.Time(item, "completed_at"), url, runId, null, excerpt));
        }
        return result;
    }

    /// <summary>Commit statuses (external CI) from <c>GET /commits/{sha}/status</c> (the "statuses" array).</summary>
    public static IReadOnlyList<CheckRun> ParseCommitStatuses(JsonElement? root)
    {
        var result = new List<CheckRun>();
        foreach (var item in Js.Items(root, "statuses"))
        {
            var id = Js.Long(item, "id");
            var name = CleanName(Js.Str(item, "context"));
            if (id is null || name is null) continue;

            var raw = Js.Str(item, "state");
            var state = raw switch
            {
                "success" => RunState.Success,
                "failure" or "error" => RunState.Failure,
                _ => RunState.Running,
            };
            var created = Js.Time(item, "created_at");
            var terminal = state != RunState.Running;
            result.Add(new CheckRun(
                id.Value, name, state, created, terminal ? Js.Time(item, "updated_at") ?? created : null,
                SafeUrl(Js.Str(item, "target_url")), null, null, state == RunState.Failure ? LogExcerpt.Clean(Js.Str(item, "description")) : null));
        }
        return result;
    }

    /// <summary>The name of the first failed step of a job (<c>GET /actions/jobs/{id}</c>), or null.</summary>
    public static string? FailingStepOfJob(JsonElement? job)
    {
        foreach (var step in Js.Items(job, "steps"))
        {
            if (Js.Str(step, "conclusion") == "failure") return CleanName(Js.Str(step, "name"));
        }
        return null;
    }

    /// <summary>Workflow runs from <c>GET /actions/runs</c> (the "workflow_runs" array).</summary>
    public static IReadOnlyList<WorkflowRun> ParseWorkflowRuns(JsonElement? root)
    {
        var result = new List<WorkflowRun>();
        foreach (var item in Js.Items(root, "workflow_runs"))
        {
            var id = Js.Long(item, "id");
            var name = CleanName(Js.Str(item, "name"));
            var created = Js.Time(item, "created_at");
            var status = Js.Str(item, "status");
            if (id is null || name is null || created is null || status is null) continue;

            result.Add(new WorkflowRun(
                id.Value,
                name,
                RunStateOf(status, Js.Str(item, "conclusion")),
                Js.Str(item, "head_branch") ?? string.Empty,
                Js.Str(item, "head_sha") ?? string.Empty,
                Js.Str(item, "event") ?? string.Empty,
                SafeUrl(Js.Str(item, "html_url")),
                created.Value,
                Js.Time(item, "updated_at") ?? created.Value));
        }
        return result;
    }

    /// <summary>
    /// Deployments from <c>GET /deployments</c> (a JSON array). The state is Queued until <see cref="WithStatus"/> is applied.
    /// </summary>
    public static IReadOnlyList<Deployment> ParseDeployments(JsonElement? root)
    {
        var result = new List<Deployment>();
        foreach (var item in Js.Items(root))
        {
            var id = Js.Long(item, "id");
            var env = CleanName(Js.Str(item, "environment"));
            var created = Js.Time(item, "created_at");
            if (id is null || env is null || created is null) continue;
            result.Add(new Deployment(
                env, id.Value, Js.Str(item, "sha") ?? string.Empty, Js.Str(item, "ref") ?? string.Empty,
                Js.Str(item, "creator", "login") ?? string.Empty, created.Value, DeployState.Queued, string.Empty));
        }
        return result;
    }

    /// <summary>
    /// True when any status of <c>GET /deployments/{id}/statuses</c> is "success". The newest status is not enough: once a newer
    /// deployment succeeds, GitHub marks the older ones "inactive", but they did go out.
    /// </summary>
    public static bool EverSucceeded(JsonElement? statuses) =>
        Js.Items(statuses).Any(s => string.Equals(Js.Str(s, "state"), "success", StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies the newest status from <c>GET /deployments/{id}/statuses</c> (a JSON array, newest first).</summary>
    public static Deployment WithStatus(Deployment deployment, JsonElement? statuses)
    {
        var items = Js.Items(statuses);
        if (items.Count == 0) return deployment;
        var first = items[0];
        var url = SafeUrl(Js.Str(first, "log_url"));
        if (url.Length == 0) url = SafeUrl(Js.Str(first, "environment_url"));
        return deployment with { State = DeployStateOf(Js.Str(first, "state")), Url = url };
    }

    /// <summary>The latest release answer (<c>GET /releases/latest</c>), or null.</summary>
    public static ReleaseInfo? ParseRelease(JsonElement? release)
    {
        var tag = CleanName(Js.Str(release, "tag_name"));
        if (tag is null) return null;
        var at = Js.Time(release, "published_at") ?? Js.Time(release, "created_at");
        return at is null ? null : new ReleaseInfo(tag, at.Value);
    }

    /// <summary>ahead_by of a compare answer: how many commits head is ahead of base. Null when missing.</summary>
    public static int? AheadBy(JsonElement? compare)
    {
        var n = Js.Int(compare, "ahead_by");
        return n is null || n.Value < 0 ? null : n;
    }

    private static string? CleanName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Names can contain line breaks or double spaces (YAML block scalars): collapse them.
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join(" ", parts);
        var cleaned = new string(joined.Where(c => !char.IsControl(c)).ToArray());
        if (cleaned.Length > 200) cleaned = cleaned.Substring(0, 200);
        return cleaned.Length == 0 ? null : cleaned;
    }
}
