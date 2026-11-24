using Cshubben.Core.Features.Feed;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

static class Rows
{
    /// <summary>
    /// One PR as a clickable row. Colour is spent only on what needs attention: the pill shows the next action,
    /// and "everything fine" stays quiet.
    /// </summary>
    public static Element PrRow(PullRequest pr, NextAction action, bool selected, DateTimeOffset now, Action<string> onSelect, Element? badge = null, bool compact = false)
    {
        var chips = PrStatusChips.For(pr, action);
        var chipRow = chips.Count == 0 ? null : HStack(4, chips.Select(c => Chip(c, compact).WithKey(c.Text)).ToArray());
        var actionPill = action.IsActionable ? Kit.Pill(action.Label, action.Tone) : Kit.Muted(action.Label);
        var ciFailing = pr.Ci == CiState.Failure ? Kit.Pill("CI failing", Tone.Danger) : null;

        var repoLine = HStack(6,
            GitHubStyle.Icon(GitIcon.PullRequest, pr.IsDraft ? Tone.Neutral : Tone.Success, 14),
            Kit.Muted($"{pr.Repo} #{pr.Number} · {pr.Author}" + (pr.IsDraft ? " · draft" : "")).TextTrimming(TextTrimming.CharacterEllipsis));
        var sizeLine = HStack(8,
            Kit.Muted(pr.SizeLabel),
            Kit.Muted("+" + pr.Additions).Foreground(Kit.Fg(Tone.Success)),
            Kit.Muted("−" + pr.Deletions).Foreground(Kit.Fg(Tone.Danger)),
            Kit.Muted(pr.ChangedFiles + (pr.ChangedFiles == 1 ? " file" : " files")),
            compact || chipRow is null ? null : chipRow.Margin(6, 0, 0, 0));

        // A narrow list gives the title the whole width: the status chips and the action pill move to their own line below it.
        var content = compact
            ? Grid(
                columns: [GridSize.Px(40), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Avatars.Of(pr.Author, 28).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Margin(0, 2, 0, 0).Grid(column: 0),
                VStack(3,
                    TextBlock(pr.Title).TextWrapping().MaxLines(2).TextTrimming(TextTrimming.CharacterEllipsis).SemiBold(),
                    repoLine,
                    sizeLine,
                    FlexRow(chipRow, badge, ciFailing, actionPill) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 }).Grid(column: 1),
                Kit.Muted(Age.Format(pr.UpdatedAt, now)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top).Margin(8, 2, 0, 0).Grid(column: 2))
            : Grid(
                columns: [GridSize.Px(40), GridSize.Star(), GridSize.Auto, GridSize.Px(56)],
                rows: [GridSize.Auto],
                Avatars.Of(pr.Author, 28).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center).Grid(column: 0),
                VStack(2,
                    TextBlock(pr.Title).TextTrimming(TextTrimming.CharacterEllipsis).SemiBold(),
                    repoLine,
                    sizeLine).Grid(column: 1),
                HStack(8, badge, ciFailing, actionPill)
                    .VAlign(VerticalAlignment.Center).Margin(12, 0, 12, 0).Grid(column: 2),
                Kit.Muted(Age.Format(pr.UpdatedAt, now)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 3));

        var button = Button(content, () => onSelect(pr.Id))
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName($"{pr.Repo} #{pr.Number} {pr.Title}");

        return Border(button)
            .Selected(selected)
            .CornerRadius(6)
            .WithKey(pr.Id);
    }

    /// <summary>
    /// A status chip. Compact (a narrow list): just an icon, a check for Approved, "CI" for passing checks, and a comment icon
    /// with the number for open threads. The full text stays as the tooltip and the screen reader name.
    /// </summary>
    private static Element Chip(StatusChip chip, bool compact)
    {
        if (!compact) return Kit.Pill(chip.Text, chip.Tone);

        Element content = chip.Kind switch
        {
            StatusChipKind.Approved => GitHubStyle.Icon(GitIcon.Check, chip.Tone, 12),
            StatusChipKind.Threads => HStack(3,
                GitHubStyle.Icon(GitIcon.Comment, chip.Tone, 12),
                TextBlock(chip.Count.ToString()).FontSize(12).SemiBold().Foreground(Kit.Fg(chip.Tone))),
            _ => TextBlock("CI").FontSize(12).SemiBold().Foreground(Kit.Fg(chip.Tone)),
        };

        return Border(content.VAlign(VerticalAlignment.Center))
            .Background(Kit.Bg(chip.Tone)).CornerRadius(10).Padding(6, 2)
            .ToolTip(chip.Text)
            .AutomationName(chip.Text);
    }

    public static string KindGlyph(FeedKind kind) => kind switch
    {
        FeedKind.ReviewRequested => "\uE7B3",
        FeedKind.Approved => "\uE73E",
        FeedKind.ChangesRequested => "\uE70F",
        FeedKind.Commented => "\uE8BD",
        FeedKind.CiFailed => "\uE711",
        FeedKind.CiPassed => "\uE73E",
        FeedKind.Merged => "\uE73A",
        FeedKind.Opened => "\uE710",
        FeedKind.Pushed => "\uE74A",
        FeedKind.Deployed => "\uE724",
        _ => "\uE7C1",
    };

    public static Tone KindTone(FeedKind kind) => kind switch
    {
        FeedKind.ReviewRequested => Tone.Accent,
        FeedKind.ChangesRequested => Tone.Danger,
        FeedKind.CiFailed => Tone.Danger,
        FeedKind.Merged => Tone.Merged,
        FeedKind.Approved or FeedKind.CiPassed or FeedKind.Opened or FeedKind.Deployed => Tone.Success,
        _ => Tone.Neutral,
    };

    /// <summary>The icon for a feed event: GitHub's git icons where one exists, otherwise a Segoe glyph.</summary>
    public static Element KindIcon(FeedKind kind, double size = 14)
    {
        var tone = KindTone(kind);
        return kind switch
        {
            FeedKind.Merged => GitHubStyle.Icon(GitIcon.Merge, tone, size),
            FeedKind.Opened => GitHubStyle.Icon(GitIcon.PullRequest, tone, size),
            FeedKind.Pushed => GitHubStyle.Icon(GitIcon.Commit, tone, size),
            FeedKind.Commented => GitHubStyle.Icon(GitIcon.Comment, tone, size),
            FeedKind.Approved or FeedKind.CiPassed => GitHubStyle.Icon(GitIcon.Check, tone, size),
            FeedKind.CiFailed or FeedKind.ChangesRequested => GitHubStyle.Icon(GitIcon.Cross, tone, size),
            _ => Kit.Glyph(KindGlyph(kind), size - 2).Foreground(GitHubStyle.Fg(tone)).VAlign(VerticalAlignment.Center),
        };
    }

    public static Element FeedRow(FeedItem item, DateTimeOffset now)
    {
        var what = item.Title is null ? item.Detail : $"{item.Detail} · #{item.Number} {item.Title}";
        var tone = item.Personal ? KindTone(item.Kind) : Tone.Neutral;
        return Grid(
            columns: [GridSize.Px(32), GridSize.Star(), GridSize.Auto, GridSize.Px(56)],
            rows: [GridSize.Auto],
            KindIcon(item.Kind).HAlign(HorizontalAlignment.Left).Grid(column: 0),
            VStack(2,
                TextBlock(item.Actor).SemiBold(),
                TextBlock(what).TextTrimming(TextTrimming.CharacterEllipsis).Foreground(Theme.SecondaryText)).Grid(column: 1),
            Kit.Pill(item.Repo, Tone.Neutral).VAlign(VerticalAlignment.Center).Margin(12, 0).Grid(column: 2),
            Kit.Muted(Age.Format(item.At, now)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 3))
            .Padding(12, 8)
            .WithKey(item.Id);
    }

    /// <summary>First letter of a name for the avatar chip, upper case; "?" when there is none.</summary>
    public static string Initial(string name)
    {
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        }

        return "?";
    }

    /// <summary>
    /// A feed row for a group of events on one PR: unseen accent bar, avatar initial chip, verb icon with the collapsed
    /// summary ("maria pushed 3 commits, 2 comments"), repo and PR title, relative time. Groups of several events show a count
    /// and a chevron. <paramref name="onClick"/> expands a collapsed group or opens the PR.
    /// </summary>
    public static Element FeedGroupRow(FeedGroup group, DateTimeOffset now, bool expanded, Action onClick)
    {
        var tone = group.Personal ? KindTone(group.Kind) : Tone.Neutral;
        var where = group.Number is null
            ? group.Repo + (group.Newest.Detail.Length > 0 ? " · " + group.Newest.Detail : "")
            : group.Repo + " #" + group.Number + (string.IsNullOrEmpty(group.Title) ? "" : " " + group.Title);

        var lines = new List<Element>
        {
            HStack(6,
                KindIcon(group.Kind),
                TextBlock(group.ActorLabel).SemiBold(),
                TextBlock(group.Action).Foreground(Theme.SecondaryText)),
            Kit.Muted(where).TextTrimming(TextTrimming.CharacterEllipsis),
        };
        if (group.Snippet.Length > 0)
            lines.Add(Kit.Muted(group.Snippet).TextTrimming(TextTrimming.CharacterEllipsis).MaxLines(1));

        var trailing = new List<Element>();
        if (group.IsCollapsed)
        {
            trailing.Add(Kit.Pill(group.Count.ToString(), Tone.Neutral).VAlign(VerticalAlignment.Center));
            trailing.Add(Kit.Glyph(expanded ? "\uE70E" : "\uE70D", 10).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center));
        }

        trailing.Add(Kit.Muted(Age.Format(group.At, now)).VAlign(VerticalAlignment.Center));

        var content = Grid(
            columns: [GridSize.Px(3), GridSize.Px(28), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Border(Empty()).Width(3).CornerRadius(2).Background(group.IsNew ? Theme.Accent : Theme.SubtleFill).Opacity(group.IsNew ? 1 : 0).Grid(column: 0),
            Avatars.Of(group.Newest.Actor, 28).VAlign(VerticalAlignment.Center).Grid(column: 1),
            VStack(2, lines.ToArray()).VAlign(VerticalAlignment.Center).Margin(10, 0, 8, 0).Grid(column: 2),
            HStack(8, trailing.ToArray()).VAlign(VerticalAlignment.Center).Grid(column: 3));

        return Button(content, onClick)
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Padding(8, 8)
            .AutomationName((group.IsNew ? "New. " : "") + group.Summary + ", " + where)
            .WithKey(group.Key);
    }

    /// <summary>One event inside an expanded group: verb icon, what happened, time. Indented under the group row.</summary>
    public static Element FeedEventRow(FeedItem item, DateTimeOffset now, bool isNew, Action? onClick)
    {
        var tone = item.Personal ? KindTone(item.Kind) : Tone.Neutral;
        var text = item.Detail.Length > 0 ? item.Actor + " · " + item.Detail : item.Actor;
        var content = Grid(
            columns: [GridSize.Px(20), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.Glyph(KindGlyph(item.Kind), 12).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center).Grid(column: 0),
            TextBlock(text).FontSize(12).TextTrimming(TextTrimming.CharacterEllipsis)
                .Foreground(isNew ? Theme.PrimaryText : Theme.SecondaryText).VAlign(VerticalAlignment.Center).Grid(column: 1),
            Kit.Muted(Age.Format(item.At, now)).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 2));

        return Button(content, onClick ?? (() => { }))
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Padding(8, 4)
            .Margin(46, 0, 0, 0)
            .AutomationName(item.Actor + " " + item.Detail)
            .WithKey(item.Id);
    }
}
