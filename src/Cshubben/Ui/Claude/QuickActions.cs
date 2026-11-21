using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

sealed record QuickActionsProps(PullRequest Pr);

enum QuickPanel { None, Claude, Linear, Review }

/// <summary>
/// The inspector's quick actions: Approve / Request changes (reviewer role), Ask Claude, Linear issue, Save to notes and GitHub.
/// Each opens its panel below the button row. Panels stay mounted while hidden, so an answer is still there when the panel is
/// reopened. The inspector keys this component by pull request, so switching PR starts fresh.
/// </summary>
sealed class QuickActions : Component<QuickActionsProps>
{
    public override Element Render()
    {
        var pr = Props.Pr;
        var linear = IntegrationServices.Linear;

        var (open, setOpen) = UseState(QuickPanel.None);
        var (verdict, setVerdict) = UseState(ReviewVerdict.Approve);
        var (nonce, setNonce) = UseState(0);
        var (notice, setNotice) = UseState("");
        var (noticeTone, setNoticeTone) = UseState(Tone.Success);
        var (savedNote, setSavedNote) = UseState(false);

        void Toggle(QuickPanel panel) => setOpen(open == panel ? QuickPanel.None : panel);

        void OpenReview(ReviewVerdict v)
        {
            if (open == QuickPanel.Review && verdict == v)
            {
                setOpen(QuickPanel.None);
                return;
            }

            setVerdict(v);
            setNonce(nonce + 1);
            setOpen(QuickPanel.Review);
        }

        void SaveToNotes()
        {
            try
            {
                NotesPage.Store.SaveAsync(Note.FromPullRequest(pr)).GetAwaiter().GetResult();
                setSavedNote(true);
                setNotice("Saved a note about this pull request. Find it on the Notes page.");
                setNoticeTone(Tone.Success);
            }
            catch (Exception ex)
            {
                setNotice("Could not save the note: " + ex.Message);
                setNoticeTone(Tone.Danger);
            }
        }

        void OpenGitHub()
        {
            if (ExternalActions.OpenUrl(pr.Url)) return;
            setNotice("Could not open " + pr.Url);
            setNoticeTone(Tone.Danger);
        }

        Element Choice(string label, bool selected, Action onClick, string name) =>
            Border(Button(label, onClick).SubtleButton().AutomationName(name))
                .Selected(selected).CornerRadius(6);

        var linearId = linear.IdentifierFor(pr);
        var isReviewer = pr.Role == PrRole.ReviewRequested;

        var row = FlexRow(
            isReviewer ? Choice("Approve", open == QuickPanel.Review && verdict == ReviewVerdict.Approve, () => OpenReview(ReviewVerdict.Approve), "Approve this pull request") : null,
            isReviewer ? Choice("Request changes", open == QuickPanel.Review && verdict == ReviewVerdict.RequestChanges, () => OpenReview(ReviewVerdict.RequestChanges), "Request changes") : null,
            Choice("✦ Ask Claude", open == QuickPanel.Claude, () => Toggle(QuickPanel.Claude), "Ask Claude about this pull request"),
            Choice("◇ " + (linearId ?? "Linear issue"), open == QuickPanel.Linear, () => Toggle(QuickPanel.Linear), "Linear issue"),
            Choice(savedNote ? "✓ Saved" : "✎ Save to notes", false, SaveToNotes, "Save a note about this pull request"),
            Choice("↗ GitHub", false, OpenGitHub, "Open on GitHub"),
            CopyLinkButton.For(pr.Url),
            CopyLinkButton.For(pr.Url, "Copy Markdown", pr.Repo + "#" + pr.Number + " " + pr.Title))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };

        Element? noticeLine = notice.Length > 0
            ? Kit.Muted(notice).Foreground(Kit.Fg(noticeTone)).TextWrapping().Margin(0, 8, 0, 0)
            : null;

        var claudePanel = Component<ClaudePanel, ClaudePanelProps>(new ClaudePanelProps(pr));
        var linearPanel = Component<LinearIssuePanel, LinearPanelProps>(new LinearPanelProps(pr, open == QuickPanel.Linear));
        var reviewPanel = Component<ReviewComposer, ReviewComposerProps>(
                new ReviewComposerProps(pr, verdict, () => setOpen(QuickPanel.None)))
            .WithKey("review-" + verdict + "-" + nonce);

        return FlexColumn(
            row.WithKey("row"),
            noticeLine?.WithKey("notice"),
            Border(claudePanel).IsVisible(open == QuickPanel.Claude).WithKey("panel-claude"),
            Border(linearPanel).IsVisible(open == QuickPanel.Linear).WithKey("panel-linear"),
            Border(reviewPanel).IsVisible(open == QuickPanel.Review).WithKey("panel-review"));
    }
}
