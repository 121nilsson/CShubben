using System.Globalization;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>How a repository looks at a glance.</summary>
public enum RepoHealth
{
    /// <summary>Main is red or a notable deployment failed recently.</summary>
    Attention,

    /// <summary>Something is running or waiting.</summary>
    Running,

    /// <summary>Main is green; nothing to do.</summary>
    Healthy,

    /// <summary>No recent runs and no deployments.</summary>
    Quiet,

    /// <summary>The repository could not be read.</summary>
    Unavailable,
}

/// <summary>One repository, interpreted for display.</summary>
public sealed record RepoRow(
    RepoActivity Activity,
    RepoHealth Health,
    RunState? MainState,
    IReadOnlyList<WorkflowRun> MainRuns,
    IReadOnlyList<WorkflowRun> FailingWorkflows,
    IReadOnlyList<Deployment> Environments,
    IReadOnlyList<Deployment> FailedDeploys,
    Deployment? LastSuccessfulDeploy,
    TimeSpan? LastDeployAge,
    int? UnreleasedCommits,
    int? UndeployedCommits,
    string Headline,
    string DeployLine,
    string? ReleaseLine,
    DateTimeOffset? LastEvent)
{
    public string Repo => Activity.Repo;

    /// <summary>The repository name without the owner.</summary>
    public string ShortName
    {
        get
        {
            var slash = Repo.LastIndexOf('/');
            return slash < 0 ? Repo : Repo.Substring(slash + 1);
        }
    }

    /// <summary>The first failed workflow run, the one "Re-run" acts on. Null when main is not red.</summary>
    public WorkflowRun? FirstFailing => FailingWorkflows.Count > 0 ? FailingWorkflows[0] : null;
}

/// <summary>
/// Drift across repositories: default-branch CI, last successful deploy, commits not yet released or deployed,
/// and failing workflows on main. Pure; the data comes from <see cref="RepoActivity"/>.
/// </summary>
public sealed record DriftReport(
    IReadOnlyList<RepoRow> Rows,
    int NeedAttention,
    int Running,
    int Healthy,
    int Quiet,
    int Unavailable,
    int FailingWorkflows,
    int ReposBehindDeploy,
    int UnreleasedCommits)
{
    public static DriftReport Empty { get; } = new(Array.Empty<RepoRow>(), 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>One line for the overview strip, for example "2 need attention · 1 running · 3 healthy".</summary>
    public string Summary
    {
        get
        {
            if (Rows.Count == 0) return "No repositories to watch yet";
            if (NeedAttention == 0 && Running == 0 && Quiet == 0 && Unavailable == 0)
                return Rows.Count == 1 ? "The repository is healthy" : "All " + Rows.Count.ToString(CultureInfo.InvariantCulture) + " repositories are healthy";

            var parts = new List<string>();
            if (NeedAttention > 0) parts.Add(NeedAttention.ToString(CultureInfo.InvariantCulture) + (NeedAttention == 1 ? " needs attention" : " need attention"));
            if (Running > 0) parts.Add(Running.ToString(CultureInfo.InvariantCulture) + " running");
            if (Healthy > 0) parts.Add(Healthy.ToString(CultureInfo.InvariantCulture) + " healthy");
            if (Quiet > 0) parts.Add(Quiet.ToString(CultureInfo.InvariantCulture) + " quiet");
            if (Unavailable > 0) parts.Add(Unavailable.ToString(CultureInfo.InvariantCulture) + " unavailable");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Interprets and sorts the repositories: red main first, then failed deploys, running, healthy, quiet, unavailable.</summary>
    public static DriftReport Build(IEnumerable<RepoActivity> activities, DateTimeOffset now)
    {
        var rows = activities.Select(a => Interpret(a, now))
            .OrderBy(Rank)
            .ThenByDescending(r => r.LastEvent ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DriftReport(
            rows,
            rows.Count(r => r.Health == RepoHealth.Attention),
            rows.Count(r => r.Health == RepoHealth.Running),
            rows.Count(r => r.Health == RepoHealth.Healthy),
            rows.Count(r => r.Health == RepoHealth.Quiet),
            rows.Count(r => r.Health == RepoHealth.Unavailable),
            rows.Sum(r => r.FailingWorkflows.Count),
            rows.Count(r => r.UndeployedCommits is > 0),
            rows.Sum(r => r.UnreleasedCommits ?? 0));
    }

    private static int Rank(RepoRow row) => row.Health switch
    {
        RepoHealth.Attention => row.FailingWorkflows.Count > 0 ? 0 : 1,
        RepoHealth.Running => 2,
        RepoHealth.Healthy => 3,
        RepoHealth.Quiet => 4,
        _ => 5,
    };

    /// <summary>Interprets one repository.</summary>
    public static RepoRow Interpret(RepoActivity activity, DateTimeOffset now)
    {
        if (activity.Error is not null)
        {
            return new RepoRow(activity, RepoHealth.Unavailable, null,
                Array.Empty<WorkflowRun>(), Array.Empty<WorkflowRun>(), Array.Empty<Deployment>(), Array.Empty<Deployment>(),
                null, null, null, null, "Could not read this repository", "No deployment information", null, null);
        }

        var runs = RunSelection.LatestPerWorkflow(activity.MainRuns, now);
        var envs = RunSelection.LatestPerEnvironment(activity.Deployments);
        var failing = runs.Where(r => r.State == RunState.Failure).ToList();
        var failedDeploys = envs.Where(d => RunSelection.IsAlert(d, activity.DefaultBranch, now)).ToList();
        var busyRun = runs.Any(r => r.State is RunState.Running or RunState.Queued);
        var busyDeploy = envs.Any(d => d.State == DeployState.InProgress);

        var successes = activity.Deployments
            .Where(d => d.State == DeployState.Success && !RunSelection.IsNoiseEnvironment(d.Environment))
            .ToList();
        var prodLike = successes.Where(d => RunSelection.IsProdLike(d.Environment)).ToList();
        var pool = prodLike.Count > 0 ? prodLike : successes;
        var lastDeploy = pool.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).FirstOrDefault();
        TimeSpan? deployAge = lastDeploy is null ? null : now - lastDeploy.CreatedAt;
        if (deployAge is { } negative && negative < TimeSpan.Zero) deployAge = TimeSpan.Zero;

        RunState? mainState = null;
        if (failing.Count > 0) mainState = RunState.Failure;
        else if (runs.Any(r => r.State == RunState.Running)) mainState = RunState.Running;
        else if (runs.Any(r => r.State == RunState.Queued)) mainState = RunState.Queued;
        else if (runs.Count > 0) mainState = RunState.Success;

        RepoHealth health;
        if (failing.Count > 0 || failedDeploys.Count > 0) health = RepoHealth.Attention;
        else if (busyRun || busyDeploy) health = RepoHealth.Running;
        else if (runs.Count == 0 && envs.Count == 0) health = RepoHealth.Quiet;
        else health = RepoHealth.Healthy;

        var headline = HeadlineOf(health, failing, failedDeploys, runs, envs, activity.DefaultBranch, now);
        var deployLine = DeployLineOf(lastDeploy, now, activity.CommitsSinceDeploy);
        var releaseLine = ReleaseLineOf(activity.LastRelease, activity.CommitsSinceRelease);

        var lastEvent = runs.Select(r => (DateTimeOffset?)r.UpdatedAt)
            .Concat(envs.Select(d => (DateTimeOffset?)d.CreatedAt))
            .Max();

        return new RepoRow(activity, health, mainState, runs, failing, envs, failedDeploys, lastDeploy, deployAge,
            activity.CommitsSinceRelease, activity.CommitsSinceDeploy, headline, deployLine, releaseLine, lastEvent);
    }

    private static string HeadlineOf(
        RepoHealth health,
        IReadOnlyList<WorkflowRun> failing,
        IReadOnlyList<Deployment> failedDeploys,
        IReadOnlyList<WorkflowRun> runs,
        IReadOnlyList<Deployment> envs,
        string defaultBranch,
        DateTimeOffset now)
    {
        var branch = string.IsNullOrEmpty(defaultBranch) ? "main" : defaultBranch;
        switch (health)
        {
            case RepoHealth.Attention:
            {
                if (failing.Count > 0) return branch + " is red: " + Names(failing.Select(r => r.Name).ToList());
                var d = failedDeploys[0];
                return "Deploy to " + d.Environment + " failed " + PipelineFormat.Ago(d.CreatedAt, now);
            }
            case RepoHealth.Running:
            {
                var busy = runs.Where(r => r.State is RunState.Running or RunState.Queued).Select(r => r.Name).ToList();
                if (busy.Count > 0) return "Running: " + Names(busy);
                var deploying = envs.Where(d => d.State == DeployState.InProgress).Select(d => d.Environment).ToList();
                return "Deploying to " + Names(deploying);
            }
            case RepoHealth.Healthy:
                return runs.Count > 0 ? branch + " is green" : "No recent runs, deployments look fine";
            default:
                return "No recent activity";
        }
    }

    private static string Names(IReadOnlyList<string> names)
    {
        var shown = string.Join(", ", names.Take(2));
        return names.Count > 2 ? shown + " +" + (names.Count - 2).ToString(CultureInfo.InvariantCulture) : shown;
    }

    private static string DeployLineOf(Deployment? last, DateTimeOffset now, int? undeployed)
    {
        if (last is null) return "No successful deployment yet";
        var text = "Deployed to " + last.Environment + " " + PipelineFormat.Ago(last.CreatedAt, now);
        if (undeployed is > 0) text += " · " + PipelineFormat.Count(undeployed.Value, "commit") + " not deployed";
        return text;
    }

    private static string? ReleaseLineOf(ReleaseInfo? release, int? sinceRelease)
    {
        if (release is null) return null;
        if (sinceRelease is null) return "Latest release " + release.Tag;
        return sinceRelease.Value == 0
            ? release.Tag + " is up to date with the default branch"
            : PipelineFormat.Count(sinceRelease.Value, "commit") + " since " + release.Tag;
    }
}
