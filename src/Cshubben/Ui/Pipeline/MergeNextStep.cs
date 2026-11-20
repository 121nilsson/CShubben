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

sealed record MergeNextStepProps(PullRequest Pr, NextAction Action);

/// <summary>
/// The inspector's NEXT STEP card when the next action is "Merge". The first click on the big button opens a confirmation
/// (merge method, short head sha, checks, approvals, open threads, blockers); the second click, on the confirm button, merges.
/// While the request is running the buttons are off and a second submit is ignored. Everything goes through
/// <see cref="IMergeActions"/>, which only simulates in demo mode.
/// </summary>
sealed class MergeNextStep : Component<MergeNextStepProps>
{
    public override Element Render()
    {
        var pr = Props.Pr;
        var action = Props.Action;
        var merge = PipelineServices.Merge;
        var data = PipelineServices.Data;

        var (flow, setFlow) = UseState(MergeFlowState.Initial, true);
        var (dry, setDry) = UseState(false, true);
        var (tick, setTick) = UseState(0, true);
        var inFlight = UseRef(false);

        var wantFacts = flow.Step is MergeStep.Confirming or MergeStep.Submitting or MergeStep.Failed;

        // The state right now, not the one from the list: checks and reviews may have changed since the last refresh.
        var facts = UseResource<MergeReadiness?>(
            async ct =>
            {
                if (!wantFacts) return null;
                var checksTask = data.GetChecksAsync(pr, ct);
                var conversationTask = data.GetConversationAsync(pr, ct);
                await Task.WhenAll(checksTask, conversationTask);
                var timeline = PipelineTimeline.Build(await checksTask, DateTimeOffset.UtcNow);
                return MergeReadiness.Evaluate(MergeFacts.From(pr, timeline, await conversationTask));
            },
            new object[] { data, pr.Id, wantFacts, tick });

        var readiness = Parts.Peek(facts);

        async Task Confirm()
        {
            // Never submit twice: the ref is checked synchronously, the state follows.
            if (inFlight.Current || !flow.CanConfirm) return;
            inFlight.Current = true;
            var submitting = flow.BeginSubmit();
            setFlow(submitting);
            try
            {
                ActionOutcome outcome;
                try
                {
                    outcome = await merge.MergeAsync(pr, flow.Method, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    outcome = new ActionOutcome(false, false, ex.Message);
                }

                setDry(outcome.DryRun);
                setFlow(outcome.Ok ? submitting.Succeed(outcome.Message) : submitting.Fail(outcome.Message));
                if (outcome.Ok && !outcome.DryRun) PipelineServices.Merged(pr);
            }
            finally
            {
                inFlight.Current = false;
            }
        }

        Element body;
        if (flow.Step == MergeStep.Done)
        {
            body = VStack(6,
                TextBlock(dry ? "✓ Merge simulated" : "✓ Merged").FontSize(16).SemiBold().Foreground(Kit.Fg(Tone.Success)),
                Kit.Muted(flow.Message).TextWrapping());
        }
        else if (!flow.IsOpen)
        {
            body = VStack(10,
                Button(action.Label, () => setFlow(flow.Open()))
                    .AccentButton().HAlign(HorizontalAlignment.Stretch).AutomationName(action.Label),
                Kit.Muted(action.Reason).TextWrapping());
        }
        else
        {
            body = ConfirmPanel(pr, flow, readiness, facts, merge.IsDryRun, setFlow, () => _ = Confirm(), () => setTick(tick + 1));
        }

        return Border(VStack(10, Kit.Muted("NEXT STEP"), body))
            .Background(Kit.Bg(flow.Step == MergeStep.Failed ? Tone.Danger : action.Tone)).CornerRadius(8).Padding(16);
    }

    private static Element ConfirmPanel(
        PullRequest pr,
        MergeFlowState flow,
        MergeReadiness? readiness,
        AsyncValue<MergeReadiness?> facts,
        bool dryRun,
        Action<MergeFlowState> setFlow,
        Action confirm,
        Action retry)
    {
        var submitting = flow.Step == MergeStep.Submitting;

        var choices = MergeMethods.All
            .Select(m => PipelineKit.Choice(MergeMethods.Short(m), flow.Method == m, () => setFlow(flow.Choose(m)),
                MergeMethods.Label(m), !submitting).WithKey(m.ToString()))
            .ToArray();
        var methods = FlexRow(choices) with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };

        Element factsView;
        if (readiness is null)
        {
            var failure = Parts.ErrorOf(facts);
            factsView = failure is null
                ? Parts.Busy("Checking the latest state…")
                : VStack(8,
                    Parts.Banner("Could not check the pull request", failure, Tone.Danger),
                    Button("Try again", retry).HAlign(HorizontalAlignment.Left));
        }
        else
        {
            factsView = FactsView(readiness);
        }

        var canConfirm = readiness is { CanMerge: true } && flow.CanConfirm && !submitting;
        var confirmLabel = readiness is { NeedsOverride: true }
            ? "Merge anyway: " + MergeMethods.Label(flow.Method)
            : "Confirm: " + MergeMethods.Label(flow.Method);

        return Card(VStack(12,
            BodyStrong("Merge " + pr.Repo + " #" + pr.Number),
            VStack(6, Parts.Label("Merge method"), methods, Kit.Muted(MergeMethods.Describe(flow.Method)).TextWrapping()),
            factsView,
            flow.Step == MergeStep.Failed ? Parts.Banner("Merge failed", flow.Message, Tone.Danger) : null,
            FlexRow(
                Button(submitting ? "Merging…" : confirmLabel, confirm)
                    .AccentButton().IsEnabled(canConfirm).AutomationName("Confirm merge"),
                Button("Cancel", () => setFlow(flow.Cancel())).SubtleButton().IsEnabled(!submitting).AutomationName("Cancel merge"),
                submitting ? Parts.Busy("Merging…") : null)
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 },
            dryRun ? Kit.Muted("Dry run: nothing is sent to GitHub.").TextWrapping() : null));
    }

    private static Element FactsView(MergeReadiness r)
    {
        Element Fact(bool ok, string text) =>
            PipelineKit.Line(
                Kit.Glyph(ok ? PipelineKit.CheckGlyph : PipelineKit.WarningGlyph, 13)
                    .Foreground(ok ? Kit.Fg(Tone.Success) : Kit.Fg(Tone.Warning)).HAlign(HorizontalAlignment.Center),
                TextBlock(text).TextWrapping(),
                "");

        var rows = new List<Element>
        {
            PipelineKit.Line(
                Kit.Glyph(PipelineKit.CheckGlyph, 13).Foreground(Theme.SecondaryText).HAlign(HorizontalAlignment.Center),
                HStack(8, TextBlock("Head").Foreground(Theme.SecondaryText), PipelineKit.Mono(r.ShortSha.Length == 0 ? "unknown" : r.ShortSha, 13)),
                "").WithKey("head"),
            Fact(r.ChecksGreen, r.ChecksText).WithKey("checks"),
            Fact(r.Approved, r.ApprovalsText).WithKey("approvals"),
            Fact(r.Facts.UnresolvedThreads == 0, r.ThreadsText).WithKey("threads"),
        };

        foreach (var blocker in r.Blockers)
            rows.Add(PipelineKit.Notice(blocker.IsHard ? Tone.Danger : Tone.Warning,
                blocker.IsHard ? "Cannot merge" : "Heads up", blocker.Text).WithKey("blocker-" + blocker.Kind));

        return FlexColumn(rows.ToArray()) with { RowGap = 6 };
    }
}
