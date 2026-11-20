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

sealed record ConversationTabProps(PullRequest Pr);

/// <summary>
/// The inspector's Conversation tab: comments, reviews, inline threads, commits and events in one compact timeline, oldest first.
/// Unresolved threads are highlighted. A reply box at the bottom posts a comment through <see cref="IPipelineData"/> (dry run in demo).
/// </summary>
sealed class ConversationTab : Component<ConversationTabProps>
{
    public override Element Render()
    {
        var pr = Props.Pr;
        var data = PipelineServices.Data;

        var (tick, setTick) = UseState(0, true);
        var (showActivity, setShowActivity) = UseState(true);
        var (text, setText) = UseState("");
        var (sending, setSending) = UseState(false, true);
        var (outcome, setOutcome) = UseState<ActionOutcome?>(null, true);
        var inFlight = UseRef(false);

        var resource = UseResource<PrConversation>(
            async ct => await data.GetConversationAsync(pr, ct),
            new object[] { data, pr.Id, pr.UpdatedAt, tick });

        async Task Send(string body)
        {
            if (inFlight.Current) return;
            inFlight.Current = true;
            setSending(true);
            setOutcome(null);
            try
            {
                var result = await data.ReplyAsync(pr, body, CancellationToken.None);
                setOutcome(result);
                if (result.Ok && !result.DryRun)
                {
                    setText("");
                    setTick(tick + 1);
                }
            }
            catch (Exception ex)
            {
                setOutcome(new ActionOutcome(false, false, ex.Message));
            }
            finally
            {
                inFlight.Current = false;
                setSending(false);
            }
        }

        var conversation = Parts.Peek(resource);
        Element timeline;
        if (conversation is null)
        {
            var failure = Parts.ErrorOf(resource);
            timeline = failure is null
                ? Card(Parts.Busy("Loading the conversation…"))
                : Card(VStack(10,
                    Parts.Banner("Could not load the conversation", failure, Tone.Danger),
                    Button("Try again", () => setTick(tick + 1)).HAlign(HorizontalAlignment.Left)));
        }
        else
        {
            timeline = TimelineView(pr, conversation, showActivity, setShowActivity);
        }

        var canSend = !sending && text.Trim().Length > 0;
        var composer = Card(VStack(8,
            TextBox(text, setText, placeholderText: "Reply to the conversation")
                .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(72),
            FlexRow(
                Button("Reply", () => _ = Send(text)).AccentButton().IsEnabled(canSend).AutomationName("Post a comment"),
                sending ? Parts.Busy("Sending…") : null,
                Button("Open on GitHub ↗", () => ExternalActions.OpenUrl(pr.Url)).SubtleButton().AutomationName("Open the conversation on GitHub"))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 },
            data.IsDryRun ? Kit.Muted("Dry run: replies are simulated and nothing is sent to GitHub.").TextWrapping() : null,
            outcome is null
                ? null
                : outcome.Ok
                    ? Kit.Muted(outcome.Message).Foreground(Kit.Fg(Tone.Success)).TextWrapping()
                    : Parts.Banner("Not sent", outcome.Message, Tone.Danger)));

        return FlexColumn(timeline, composer) with { RowGap = 12 };
    }

    private static Element TimelineView(PullRequest pr, PrConversation conversation, bool showActivity, Action<bool> setShowActivity)
    {
        var now = PipelineKit.Now();
        var open = conversation.UnresolvedThreads;

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(6,
                Kit.Muted(conversation.Summary).TextWrapping(),
                open > 0 ? Kit.Pill(open + (open == 1 ? " thread to resolve" : " threads to resolve"), Tone.Warning).HAlign(HorizontalAlignment.Left) : null).Grid(column: 0),
            ToggleButton("Commits & events", showActivity, v => setShowActivity(v)).VAlign(VerticalAlignment.Top).Margin(8, 0, 0, 0).Grid(column: 1));

        var items = conversation.Visible(showActivity);
        var rows = new List<Element>();
        foreach (var item in items)
            rows.Add(ItemRow(item, now).WithKey(item.Id));

        Element list = rows.Count == 0
            ? Kit.EmptyNote("Nothing here yet. Be the first to comment.")
            : FlexColumn(rows.ToArray()) with { RowGap = 10 };

        return Card(VStack(12, header, list));
    }

    private static Element ItemRow(ConversationItem item, DateTimeOffset now)
    {
        var ago = PipelineFormat.Ago(item.At, now);
        switch (item.Kind)
        {
            case ConversationKind.Commit:
                return Compact("↑", Tone.Neutral,
                    item.Author + " " + ConversationBuilder.Describe(item) + (item.Sha is null ? "" : " · " + PipelineFormat.ShortSha(item.Sha)) + (item.Title.Length == 0 ? "" : " " + item.Title),
                    ago);

            case ConversationKind.Event:
                return Compact("•", Tone.Neutral, item.Author + " " + item.Title, ago);

            case ConversationKind.Thread:
                return ThreadRow(item, ago);

            default:
                return PostRow(item, ago);
        }
    }

    /// <summary>One muted line for commits, events and resolved threads.</summary>
    private static Element Compact(string glyph, Tone tone, string text, string ago) =>
        Grid(
            columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            TextBlock(glyph).Foreground(Kit.Fg(tone)).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Grid(column: 0),
            TextBlock(text).FontSize(12).Foreground(Theme.SecondaryText).TextTrimming(TextTrimming.CharacterEllipsis)
                .VAlign(VerticalAlignment.Center).Grid(column: 1),
            Kit.Muted(ago).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 2));

    /// <summary>A comment or a review: initial badge, author line with state chip, markdown body.</summary>
    private static Element PostRow(ConversationItem item, string ago)
    {
        var chip = item.Kind == ConversationKind.Review
            ? item.Review switch
            {
                ReviewStateKind.Approved => Kit.Pill("Approved", Tone.Success),
                ReviewStateKind.ChangesRequested => Kit.Pill("Changes requested", Tone.Warning),
                ReviewStateKind.Dismissed => Kit.Pill("Dismissed", Tone.Neutral),
                _ => Kit.Pill("Reviewed", Tone.Neutral),
            }
            : null;

        var head = FlexRow(
            TextBlock(item.Author).SemiBold(),
            item.IsBot ? Kit.Pill("bot", Tone.Neutral) : null,
            chip,
            Kit.Muted(item.Kind == ConversationKind.Comment ? "commented · " + ago : ago).VAlign(VerticalAlignment.Center))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 2 };

        return Grid(
            columns: [GridSize.Px(32), GridSize.Star()],
            rows: [GridSize.Auto],
            PipelineKit.Initial(item.Author).VAlign(VerticalAlignment.Top).Grid(column: 0),
            VStack(4,
                head,
                item.Body.Length == 0 ? null : Kit.Md(item.Body)).Grid(column: 1));
    }

    /// <summary>An inline review thread. Unresolved ones are highlighted and show the replies; resolved ones collapse to a line.</summary>
    private static Element ThreadRow(ConversationItem item, string ago)
    {
        var where = (item.Path ?? "a thread") + (item.Line is { } line ? ":" + line : "");

        if (item.Resolved)
        {
            return Compact("✓", Tone.Success,
                "Resolved: " + item.Author + " on " + where + (item.Replies.Count > 0 ? " · " + PipelineFormat.Count(item.Replies.Count + 1, "comment") : ""),
                ago);
        }

        var replies = new List<Element>();
        for (var i = 0; i < item.Replies.Count; i++)
        {
            var reply = item.Replies[i];
            replies.Add(VStack(2,
                HStack(8, TextBlock(reply.Author).SemiBold().FontSize(12), Kit.Muted("replied")),
                reply.Body.Length == 0 ? null : Kit.Md(reply.Body)).WithKey("reply-" + i));
        }

        return Border(VStack(8,
                FlexRow(
                    Kit.Pill("Unresolved", Tone.Warning),
                    item.Outdated ? Kit.Pill("Outdated", Tone.Neutral) : null,
                    Kit.Muted(ago).VAlign(VerticalAlignment.Center)) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 },
                PipelineKit.Mono(where, 12).Foreground(Theme.SecondaryText).TextTrimming(TextTrimming.CharacterEllipsis),
                HStack(8, TextBlock(item.Author).SemiBold(), Kit.Muted("wrote")),
                item.Body.Length == 0 ? null : Kit.Md(item.Body),
                replies.Count == 0 ? null : FlexColumn(replies.ToArray()) with { RowGap = 6 }))
            .Background(Kit.Bg(Tone.Warning)).CornerRadius(8).Padding(12, 10);
    }
}
