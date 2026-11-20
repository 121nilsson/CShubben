using System.Globalization;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>Overall state of the checks on a commit.</summary>
public enum PipelineState { None, Passing, Failing, Running, Queued }

/// <summary>One check in the timeline: the latest attempt of a check name, with what happened before.</summary>
/// <param name="Attempts">How many runs of this check were seen (retries included).</param>
/// <param name="Flaky">The check failed earlier but passed on a later attempt.</param>
public sealed record TimelineStage(
    string Name,
    RunState State,
    TimeSpan? Duration,
    DateTimeOffset? StartedAt,
    string Url,
    int Attempts,
    bool Flaky,
    string? FailingStep,
    string? Excerpt,
    long? WorkflowRunId);

/// <summary>
/// Checks on a commit as an ordered timeline: stages in start order with durations, the failing step,
/// flaky-retry detection and an overall state.
/// </summary>
public sealed record PipelineTimeline(
    IReadOnlyList<TimelineStage> Stages,
    PipelineState Overall,
    TimeSpan? WallClock)
{
    public static PipelineTimeline Empty { get; } = new(Array.Empty<TimelineStage>(), PipelineState.None, null);

    public int Total => Stages.Count;

    public int Passed => Stages.Count(s => s.State == RunState.Success);

    public int FailedCount => Stages.Count(s => s.State == RunState.Failure);

    /// <summary>Stages that are still running or waiting.</summary>
    public int ActiveCount => Stages.Count(s => s.State is RunState.Running or RunState.Queued);

    public int FlakyCount => Stages.Count(s => s.Flaky);

    /// <summary>The first failing stage in start order, or null.</summary>
    public TimelineStage? FirstFailing => Stages.FirstOrDefault(s => s.State == RunState.Failure);

    /// <summary>The longest stage duration, used to scale bars. Zero when nothing has finished.</summary>
    public TimeSpan Longest => Stages.Select(s => s.Duration ?? TimeSpan.Zero).DefaultIfEmpty(TimeSpan.Zero).Max();

    /// <summary>Distinct workflow runs that contain a failed stage. These are what "Re-run failed jobs" acts on.</summary>
    public IReadOnlyList<long> FailedRunIds =>
        Stages.Where(s => s.State == RunState.Failure && s.WorkflowRunId is not null)
            .Select(s => s.WorkflowRunId!.Value)
            .Distinct()
            .ToList();

    /// <summary>One sentence for the header of the pipeline tab.</summary>
    public string Summary
    {
        get
        {
            var n = Total;
            var checks = n == 1 ? "1 check" : n.ToString(CultureInfo.InvariantCulture) + " checks";
            switch (Overall)
            {
                case PipelineState.None:
                    return "No checks reported yet";
                case PipelineState.Passing:
                {
                    var text = n == 1 ? "The check passed" : "All " + checks + " passed";
                    if (WallClock is { } wall) text += " in " + PipelineFormat.Duration(wall);
                    if (FlakyCount > 0) text += " · " + FlakyCount.ToString(CultureInfo.InvariantCulture) + " flaky (passed on retry)";
                    return text;
                }
                case PipelineState.Failing:
                {
                    var names = Stages.Where(s => s.State == RunState.Failure).Select(s => s.Name).ToList();
                    var shown = string.Join(", ", names.Take(2));
                    if (names.Count > 2) shown += " +" + (names.Count - 2).ToString(CultureInfo.InvariantCulture) + " more";
                    return FailedCount.ToString(CultureInfo.InvariantCulture) + " of " + checks + " failing (" + shown + ")";
                }
                default:
                {
                    var done = n - ActiveCount;
                    var waiting = Overall == PipelineState.Queued ? "waiting to start" : "still running";
                    return done.ToString(CultureInfo.InvariantCulture) + " of " + checks + " done, "
                        + ActiveCount.ToString(CultureInfo.InvariantCulture) + " " + waiting;
                }
            }
        }
    }

    /// <summary>
    /// Builds the timeline from every check run seen on a commit (all attempts, not only the latest).
    /// Runs with the same name are attempts of one check; the newest attempt decides its state.
    /// </summary>
    public static PipelineTimeline Build(IEnumerable<CheckRun> runs, DateTimeOffset now)
    {
        var all = runs.ToList();
        if (all.Count == 0) return Empty;

        var groups = new Dictionary<string, List<CheckRun>>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var run in all)
        {
            if (!groups.TryGetValue(run.Name, out var list))
            {
                list = new List<CheckRun>();
                groups[run.Name] = list;
                names.Add(run.Name);
            }
            list.Add(run);
        }

        var stages = new List<TimelineStage>();
        foreach (var name in names)
        {
            var attempts = groups[name]
                .OrderBy(r => r.StartedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(r => r.Id)
                .ToList();
            var latest = attempts[attempts.Count - 1];
            var failedBefore = attempts.Take(attempts.Count - 1).Any(r => r.State == RunState.Failure);
            stages.Add(new TimelineStage(
                name,
                latest.State,
                latest.Duration,
                attempts[0].StartedAt,
                latest.Url,
                attempts.Count,
                latest.State == RunState.Success && failedBefore,
                latest.FailingStep,
                latest.Excerpt,
                latest.WorkflowRunId));
        }

        var ordered = stages
            .OrderBy(s => s.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        return new PipelineTimeline(ordered, OverallOf(ordered), WallClockOf(all, ordered, now));
    }

    private static PipelineState OverallOf(IReadOnlyList<TimelineStage> stages)
    {
        if (stages.Count == 0) return PipelineState.None;
        if (stages.Any(s => s.State == RunState.Failure)) return PipelineState.Failing;
        if (stages.Any(s => s.State == RunState.Running)) return PipelineState.Running;
        if (stages.Any(s => s.State == RunState.Queued)) return PipelineState.Queued;
        return stages.Any(s => s.State == RunState.Success) ? PipelineState.Passing : PipelineState.None;
    }

    private static TimeSpan? WallClockOf(List<CheckRun> all, IReadOnlyList<TimelineStage> stages, DateTimeOffset now)
    {
        var starts = all.Where(r => r.StartedAt is not null).Select(r => r.StartedAt!.Value).ToList();
        if (starts.Count == 0) return null;
        var start = starts.Min();

        DateTimeOffset end;
        if (stages.Any(s => s.State is RunState.Running or RunState.Queued))
        {
            end = now;
        }
        else
        {
            var ends = all.Where(r => r.CompletedAt is not null).Select(r => r.CompletedAt!.Value).ToList();
            if (ends.Count == 0) return null;
            end = ends.Max();
        }

        return end < start ? null : end - start;
    }
}
