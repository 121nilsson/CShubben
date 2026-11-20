using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Cshubben.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

sealed record ReviewComposerProps(PullRequest Pr, ReviewVerdict Verdict, Action OnClose);

/// <summary>
/// The small confirmation step before Approve or Request changes is sent: the verdict, a comment box and a Send button.
/// Nothing is sent before Send is pressed, and demo mode never sends.
/// </summary>
sealed class ReviewComposer : Component<ReviewComposerProps>
{
    public override Element Render()
    {
        var pr = Props.Pr;
        var verdict = Props.Verdict;
        var close = Props.OnClose;
        var actions = IntegrationServices.Actions;
        var approve = verdict == ReviewVerdict.Approve;

        var (text, setText) = UseState("");
        var (sending, setSending) = UseState(false, true);
        var (outcome, setOutcome) = UseState<ActionOutcome?>(null, true);

        async Task Send(string comment)
        {
            setSending(true);
            try
            {
                setOutcome(await actions.SubmitReviewAsync(pr, verdict, comment, CancellationToken.None));
            }
            catch (Exception ex)
            {
                setOutcome(new ActionOutcome(false, false, ex.Message));
            }

            setSending(false);
        }

        var needsText = !approve && text.Trim().Length == 0;
        var heading = (approve ? "Approve" : "Request changes") + " · " + pr.Repo + " #" + pr.Number;
        var done = outcome is { Ok: true };

        Element footer;
        if (done)
        {
            footer = FlexRow(Button("Done", close).AccentButton()) with { ColumnGap = 8 };
        }
        else
        {
            footer = FlexRow(
                Button(approve ? "Send approval" : "Send request for changes", () => _ = Send(text))
                    .AccentButton().IsEnabled(!sending && !needsText),
                Button("Cancel", close).SubtleButton().IsEnabled(!sending),
                sending ? Parts.Busy("Sending…") : null)
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 };
        }

        Element? result = null;
        if (outcome is not null)
        {
            result = outcome.Ok
                ? Kit.Muted(outcome.Message).Foreground(Kit.Fg(Tone.Success)).TextWrapping()
                : Parts.Banner("Not sent", outcome.Message, Tone.Danger);
        }

        return Card(VStack(10,
                BodyStrong(heading),
                Kit.Muted(approve
                    ? "This posts an approving review on GitHub. A comment is optional."
                    : "This posts a review that asks for changes on GitHub. Say what needs to change.").TextWrapping(),
                done
                    ? null
                    : TextBox(text, setText, placeholderText: approve ? "Optional comment" : "What needs to change? (required)")
                        .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(88),
                actions.IsDryRun ? Kit.Muted("Demo mode: nothing is sent to GitHub.") : null,
                result,
                footer))
            .Margin(0, 8, 0, 0);
    }
}
