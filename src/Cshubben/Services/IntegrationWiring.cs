using System.Globalization;
using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;
using Cshubben.Ui.Assist;

namespace Cshubben.Services;

/// <summary>
/// Puts the real Claude, Linear and GitHub-action implementations behind <see cref="IntegrationServices"/>.
/// Call <see cref="Apply"/> once after <c>AppServices.Initialize</c>. In demo mode (<c>--demo</c>) it does nothing, so
/// screenshots and CI keep the demo implementations and nothing real is ever sent.
/// </summary>
static class IntegrationWiring
{
    private static bool? _attachedDryRun;

    /// <summary>
    /// Creates the live pipeline/merge adapters once the GitHub connection exists (it is created on the first load).
    /// Writes stay a dry run unless the user turned on "Allow writes to GitHub" in Settings.
    /// </summary>
    public static void AttachGitHub(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.IsDemo || services.GitHub is not { } hub) return;
        var dryRun = !services.Settings.AllowGitHubWrites;
        if (_attachedDryRun == dryRun) return;
        _attachedDryRun = dryRun;
        PipelineServices.Data = new GitHubPipelineData(hub.Rest, hub.GraphQl, hub.Writer) { DryRun = dryRun };
        PipelineServices.Merge = new GitHubMergeActions(hub.Writer, dryRun);
    }

    public static void Apply(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.ForcedDemo) return;

        IntegrationServices.Claude = RealClaudeAssistant.CreateDefault(services.Secrets, () => services.Settings);
        IntegrationServices.Linear = new RealLinearData(services.Secrets, services.Http, () => services.GitHub?.GraphQl);
        IntegrationServices.Actions = new SettingsGatedActions(services);
    }
}

/// <summary>
/// GitHub write actions that follow the "Allow writes to GitHub" setting on every call: off (the default) means a dry run.
/// The GitHub connection is looked up per call because it only exists after the first successful load.
/// </summary>
sealed class SettingsGatedActions(AppServices services) : IInspectorActions
{
    /// <summary>Stop reading the diff here; Claude does not need more and prompts have limits.</summary>
    internal const int MaxDiffChars = 150_000;

    private const int MaxPages = 3;

    public bool IsDryRun => services.GitHub is null || !services.Settings.AllowGitHubWrites;

    public Task<ActionOutcome> SubmitReviewAsync(PullRequest pr, ReviewVerdict verdict, string body, CancellationToken cancellationToken)
    {
        var hub = services.GitHub;
        if (hub is null) return Task.FromResult(new ActionOutcome(false, false, "Not connected to GitHub."));
        var inner = new GitHubInspectorActions(hub.Writer, (p, ct) => DiffAsync(hub, p, ct), dryRun: !services.Settings.AllowGitHubWrites);
        return inner.SubmitReviewAsync(pr, verdict, body, cancellationToken);
    }

    public async Task<string> GetDiffAsync(PullRequest pr, CancellationToken cancellationToken)
    {
        var hub = services.GitHub;
        if (hub is null) return string.Empty;
        try
        {
            return await DiffAsync(hub, pr, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>The changed files of a PR as one unified diff text (patches as GitHub returns them), capped at <see cref="MaxDiffChars"/>.</summary>
    private static async Task<string> DiffAsync(GitHubHubData hub, PullRequest pr, CancellationToken cancellationToken)
    {
        if (!GitHubNames.TrySplitRepo(pr.Repo, out var owner, out var name)) return string.Empty;
        var text = new StringBuilder();
        for (var page = 1; page <= MaxPages && text.Length < MaxDiffChars; page++)
        {
            var url = GitHubRest.ApiBase + "/repos/" + owner + "/" + name + "/pulls/" + pr.Number.ToString(CultureInfo.InvariantCulture)
                + "/files?per_page=100&page=" + page.ToString(CultureInfo.InvariantCulture);
            var response = await hub.Rest.GetAsync(url, null, cancellationToken).ConfigureAwait(false);
            if (response.Json is not JsonElement json || json.ValueKind != JsonValueKind.Array) break;

            var count = 0;
            foreach (var file in json.EnumerateArray())
            {
                count++;
                var path = JsonRead.Str(file, "filename");
                var patch = JsonRead.Str(file, "patch");
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(patch)) continue;
                text.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
                text.Append("--- a/").Append(path).Append('\n');
                text.Append("+++ b/").Append(path).Append('\n');
                text.Append(patch).Append('\n');
                if (text.Length >= MaxDiffChars) break;
            }

            if (count < 100) break;
        }

        return text.Length > MaxDiffChars ? text.ToString(0, MaxDiffChars) : text.ToString();
    }
}
