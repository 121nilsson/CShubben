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
        FeedKind.ReviewRequested => "👀",
        FeedKind.Approved => "✔",
        FeedKind.ChangesRequested => "✎",
        FeedKind.Commented => "💬",
        FeedKind.CiFailed => "✖",
        FeedKind.CiPassed => "✔",
        FeedKind.Merged => "⮌",
        FeedKind.Opened => "＋",
        FeedKind.Pushed => "↑",
        FeedKind.Deployed => "🚀",
        _ => "•",
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
            TextBlock(KindGlyph(item.Kind)).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center).Grid(column: 0),
            VStack(2,
                TextBlock(item.Actor).SemiBold(),
                TextBlock(what).TextTrimming(TextTrimming.CharacterEllipsis).Foreground(Theme.SecondaryText)).Grid(column: 1),
            Kit.Pill(item.Repo, Tone.Neutral).VAlign(VerticalAlignment.Center).Margin(12, 0).Grid(column: 2),
            Kit.Muted(Age.Format(item.At, now)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 3))
            .Padding(12, 8)
            .WithKey(item.Id);
    }
}
