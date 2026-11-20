using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pipeline;

sealed record PipelineTabProps(PullRequest Pr);

/// <summary>
/// The inspector's Pipeline tab: the checks of the head commit as a timeline with state icons, durations, the failing
/// step and a log excerpt, "Re-run failed jobs" and "Open on GitHub". Polls while checks are still running.
/// </summary>
sealed class PipelineTab : Component<PipelineTabProps>
{
    private const int PollSeconds = 20;

    public override Element Render()
    {
        var pr = Props.Pr;
        var data = PipelineServices.Data;

        var (tick, setTick) = UseState(0, true);
        var (busy, setBusy) = UseState(false, true);
        var (outcome, setOutcome) = UseState<ActionOutcome?>(null, true);
        var tickRef = UseRef(0);
        var inFlight = UseRef(false);

        var resource = UseResource<PipelineTimeline>(
            async ct => PipelineTimeline.Build(await data.GetChecksAsync(pr, ct), DateTimeOffset.UtcNow),
            new object[] { data, pr.Id, pr.UpdatedAt, tick });

        var timeline = Parts.Peek(resource);
        var active = timeline is { Overall: PipelineState.Running or PipelineState.Queued };

        // While checks are running, look again every few seconds.
        UseEffect(() =>
        {
            if (!active) return () => { };
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollSeconds));
                    while (await timer.WaitForNextTickAsync(cts.Token))
                    {
                        tickRef.Current += 1;
                        setTick(tickRef.Current);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The tab was closed or the checks finished.
                }
            });
            return () => cts.Cancel();
        }, new object[] { active, pr.Id });

        void Refresh()
        {
            tickRef.Current += 1;
            setTick(tickRef.Current);
        }

        async Task Rerun(IReadOnlyList<long> runIds)
        {
            if (inFlight.Current) return;
            inFlight.Current = true;
            setBusy(true);
            setOutcome(null);
            try
            {
                var result = await data.RerunFailedJobsAsync(pr.Repo, runIds, CancellationToken.None);
                setOutcome(result);
                if (result.Ok && !result.DryRun) Refresh();
            }
            catch (Exception ex)
            {
                setOutcome(new ActionOutcome(false, false, ex.Message));
            }
            finally
            {
                inFlight.Current = false;
                setBusy(false);
            }
        }

        Element content;
        if (timeline is null)
        {
            var failure = Parts.ErrorOf(resource);
            content = failure is null
                ? Card(Parts.Busy("Loading checks…"))
                : Card(VStack(10,
                    Parts.Banner("Could not load the checks", failure, Tone.Danger),
                    Button("Try again", Refresh).HAlign(HorizontalAlignment.Left)));
        }
        else
        {
            content = TimelineView(pr, timeline, data.IsDryRun, busy, outcome, Rerun, Refresh,
                Parts.IsLoading(resource) || resource is AsyncValue<PipelineTimeline>.Reloading);
        }

        return content;
    }

    private static Element TimelineView(
        PullRequest pr,
        PipelineTimeline timeline,
        bool dryRun,
        bool busy,
        ActionOutcome? outcome,
        Func<IReadOnlyList<long>, Task> rerun,
        Action refresh,
        bool reloading)
    {
        var tone = PipelineKit.ToneOf(timeline.Overall);
        var runIds = timeline.FailedRunIds;

        var summary = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(2,
                TextBlock(timeline.Summary).SemiBold().Foreground(tone == Tone.Neutral ? Theme.PrimaryText : Kit.Fg(tone)).TextWrapping(),
                Kit.Muted(timeline.Overall == PipelineState.None
                    ? "Checks appear here once CI has started."
                    : "Checks on the latest commit")).Grid(column: 0),
            (reloading
                ? ProgressRing().IsActive().Width(16).Height(16)
                : (Element)Button("Refresh", refresh).SubtleButton().AutomationName("Refresh checks"))
                .VAlign(VerticalAlignment.Top).Grid(column: 1));

        var longest = timeline.Longest;
        var rows = new List<Element>();
        foreach (var stage in timeline.Stages)
            rows.Add(StageRow(stage, longest).WithKey(stage.Name));

        var actions = FlexRow(
            runIds.Count > 0
                ? Button(busy ? "Re-running…" : "Re-run failed jobs", () => _ = rerun(runIds))
                    .AccentButton().IsEnabled(!busy).AutomationName("Re-run failed jobs")
                : null,
            Button("Open on GitHub ↗", () => ExternalActions.OpenUrl(pr.Url + "/checks")).AutomationName("Open checks on GitHub"),
            busy ? Parts.Busy("Sending…") : null)
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 };

        Element? result = null;
        if (outcome is not null)
        {
            result = outcome.Ok
                ? Kit.Muted(outcome.Message).Foreground(Kit.Fg(Tone.Success)).TextWrapping()
                : Parts.Banner("Not sent", outcome.Message, Tone.Danger);
        }

        return Card(VStack(12,
            summary,
            rows.Count == 0 ? null : FlexColumn(rows.ToArray()) with { RowGap = 10 },
            actions,
            dryRun && runIds.Count > 0 ? Kit.Muted("Dry run: re-running is simulated and nothing is sent to GitHub.").TextWrapping() : null,
            result));
    }

    private static Element StageRow(TimelineStage stage, TimeSpan longest)
    {
        var tone = PipelineKit.ToneOf(stage.State);
        var fraction = stage.Duration is { } d && longest > TimeSpan.Zero ? Math.Max(0.03, d.TotalSeconds / longest.TotalSeconds) : 0.0;

        string? sub = null;
        if (stage.State == RunState.Failure && stage.FailingStep is not null) sub = "Failed at step: " + stage.FailingStep;
        else if (stage.State == RunState.Failure) sub = "Failed";
        else if (stage.Flaky) sub = "Flaky: failed, then passed on retry (" + stage.Attempts + " attempts)";
        else if (stage.State == RunState.Running) sub = "Running…";
        else if (stage.State == RunState.Queued) sub = "Waiting to start";
        else if (stage.State is RunState.Skipped or RunState.Cancelled) sub = PipelineKit.Label(stage.State);

        var subTone = stage.State == RunState.Failure ? Tone.Danger : stage.Flaky ? Tone.Warning : Tone.Neutral;

        Element? excerpt = null;
        if (stage.State == RunState.Failure && (stage.Excerpt is not null || stage.Url.Length > 0))
        {
            excerpt = Border(VStack(8,
                    stage.Excerpt is null ? null : PipelineKit.Mono(stage.Excerpt).TextWrapping().Foreground(Theme.PrimaryText),
                    stage.Url.Length == 0
                        ? null
                        : Button("Open logs ↗", () => ExternalActions.OpenUrl(stage.Url)).SubtleButton()
                            .AutomationName("Open the logs of " + stage.Name)))
                .Background(Kit.Bg(Tone.Danger)).CornerRadius(6).Padding(10, 8);
        }

        var middle = FlexColumn(
            TextBlock(stage.Name).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
            sub is null ? null : TextBlock(sub).FontSize(12).Foreground(Kit.Fg(subTone)).TextWrapping(),
            fraction > 0 ? PipelineKit.Bar(fraction, tone).Margin(0, 2, 0, 0) : null,
            excerpt?.Margin(0, 4, 0, 0)) with { RowGap = 2 };

        return Grid(
            columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            PipelineKit.StateIcon(stage.State).VAlign(VerticalAlignment.Top).Grid(column: 0),
            middle.Grid(column: 1),
            Kit.Muted(stage.Duration is { } dur ? PipelineFormat.Duration(dur) : "").VAlign(VerticalAlignment.Top).Margin(8, 2, 0, 0).Grid(column: 2));
    }
}
