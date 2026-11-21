using Cshubben.Core.Features.Feed;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

static class Rows
{
    /// <summary>
    /// One PR as a clickable row. Colour is spent only on what needs attention: the pill shows the next action,
    /// and "everything fine" stays quiet.
    /// </summary>
    public static Element PrRow(PullRequest pr, NextAction action, bool selected, DateTimeOffset now, Action<string> onSelect)
    {
        var dotTone = action.IsActionable ? action.Tone : Tone.Neutral;
        var content = Grid(
            columns: [GridSize.Px(20), GridSize.Star(), GridSize.Auto, GridSize.Px(56)],
            rows: [GridSize.Auto],
            TextBlock("●").FontSize(10).Foreground(Kit.Fg(dotTone)).VAlign(VerticalAlignment.Center).Grid(column: 0),
            VStack(2,
                TextBlock(pr.Title).TextTrimming(TextTrimming.CharacterEllipsis).SemiBold(),
                Kit.Muted($"{pr.Repo} #{pr.Number} · {pr.Author}" + (pr.IsDraft ? " · draft" : ""))).Grid(column: 1),
            (action.IsActionable ? Kit.Pill(action.Label, action.Tone) : Kit.Muted(action.Label))
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
        FeedKind.ChangesRequested => Tone.Warning,
        FeedKind.CiFailed => Tone.Danger,
        FeedKind.Approved or FeedKind.CiPassed or FeedKind.Merged or FeedKind.Deployed => Tone.Success,
        _ => Tone.Neutral,
    };

    public static Element FeedRow(FeedItem item, DateTimeOffset now)
    {
        var what = item.Title is null ? item.Detail : $"{item.Detail} · #{item.Number} {item.Title}";
        var tone = item.Personal ? KindTone(item.Kind) : Tone.Neutral;
        return Grid(
            columns: [GridSize.Px(32), GridSize.Star(), GridSize.Auto, GridSize.Px(56)],
            rows: [GridSize.Auto],
            Kit.Glyph(KindGlyph(item.Kind), 12).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center).Grid(column: 0),
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

    private static Element AvatarChip(string name, Tone tone) =>
        Border(TextBlock(Initial(name)).FontSize(12).SemiBold().Foreground(Kit.Fg(tone))
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
            .Width(28).Height(28).CornerRadius(14).Background(Kit.Bg(tone))
            .VAlign(VerticalAlignment.Center);

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
                Kit.Glyph(KindGlyph(group.Kind), 12).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center),
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
            AvatarChip(group.Newest.Actor, tone).Grid(column: 1),
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
