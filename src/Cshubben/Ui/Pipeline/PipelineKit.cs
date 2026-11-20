using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pipeline;

/// <summary>Small building blocks shared by the pipeline and conversation tabs, the merge flow and the Drift page.</summary>
static class PipelineKit
{
    public const string CheckGlyph = "";
    public const string CancelGlyph = "";
    public const string WarningGlyph = "";
    public const string ClockGlyph = "";
    public const string MinusGlyph = "";

    public static Tone ToneOf(RunState state) => state switch
    {
        RunState.Success => Tone.Success,
        RunState.Failure => Tone.Danger,
        RunState.Running => Tone.Warning,
        _ => Tone.Neutral,
    };

    public static Tone ToneOf(PipelineState state) => state switch
    {
        PipelineState.Passing => Tone.Success,
        PipelineState.Failing => Tone.Danger,
        PipelineState.Running => Tone.Warning,
        _ => Tone.Neutral,
    };

    public static Tone ToneOf(RepoHealth health) => health switch
    {
        RepoHealth.Attention => Tone.Danger,
        RepoHealth.Running => Tone.Warning,
        RepoHealth.Healthy => Tone.Success,
        _ => Tone.Neutral,
    };

    public static RunState AsRunState(DeployState state) => state switch
    {
        DeployState.Success => RunState.Success,
        DeployState.Failure => RunState.Failure,
        DeployState.InProgress => RunState.Running,
        DeployState.Queued => RunState.Queued,
        _ => RunState.Skipped,
    };

    public static string Label(RunState state) => state switch
    {
        RunState.Success => "Passed",
        RunState.Failure => "Failed",
        RunState.Running => "Running",
        RunState.Queued => "Waiting",
        RunState.Cancelled => "Cancelled",
        _ => "Skipped",
    };

    /// <summary>
    /// The state icon in a fixed 24 px box: a check, a cross, a spinner for running, a clock for waiting and a dash for skipped.
    /// </summary>
    public static Element StateIcon(RunState state)
    {
        Element icon = state switch
        {
            RunState.Success => Kit.Glyph(CheckGlyph, 14).Foreground(Kit.Fg(Tone.Success)),
            RunState.Failure => Kit.Glyph(CancelGlyph, 14).Foreground(Kit.Fg(Tone.Danger)),
            RunState.Running => ProgressRing().IsActive().Width(14).Height(14),
            RunState.Queued => Kit.Glyph(ClockGlyph, 14).Foreground(Theme.SecondaryText),
            _ => Kit.Glyph(MinusGlyph, 14).Foreground(Theme.SecondaryText),
        };
        return Border(icon.HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)).Width(24).Height(24);
    }

    /// <summary>A round badge with the first letter of a login.</summary>
    public static Element Initial(string login)
    {
        var letter = string.IsNullOrEmpty(login) ? "?" : login.Substring(0, 1).ToUpperInvariant();
        return Border(TextBlock(letter).FontSize(11).SemiBold().Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
            .Width(24).Height(24).CornerRadius(12).Background(Theme.SubtleFill);
    }

    /// <summary>A thin bar: <paramref name="fraction"/> of the width is filled with the tone colour.</summary>
    public static Element Bar(double fraction, Tone tone)
    {
        var f = Math.Min(1.0, Math.Max(0.0, fraction));
        return Grid(
            columns: [GridSize.Star(Math.Max(f, 0.0001)), GridSize.Star(Math.Max(1.0 - f, 0.0001))],
            rows: [GridSize.Auto],
            Border(Empty()).Height(3).CornerRadius(2).Background(Kit.Fg(tone)).Grid(column: 0),
            Border(Empty()).Height(3).Grid(column: 1));
    }

    /// <summary>A row with a small icon-like leading element, text that trims, and a trailing muted value.</summary>
    public static Element Line(Element leading, Element text, string trailing) =>
        Grid(
            columns: [GridSize.Px(24), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            leading.VAlign(VerticalAlignment.Center).Grid(column: 0),
            text.VAlign(VerticalAlignment.Center).Grid(column: 1),
            Kit.Muted(trailing).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 2));

    /// <summary>Monospace text for logs and commit ids.</summary>
    public static TextBlockElement Mono(string text, double size = 12) =>
        TextBlock(text).FontFamily("Cascadia Mono, Consolas").FontSize(size);

    /// <summary>A message in a tinted box with the tone's colour on the title only.</summary>
    public static Element Notice(Tone tone, string title, string message) =>
        Border(VStack(2,
                TextBlock(title).SemiBold().Foreground(Kit.Fg(tone)),
                Kit.Muted(message).TextWrapping()))
            .Background(Kit.Bg(tone)).CornerRadius(8).Padding(12, 10);

    /// <summary>A bordered choice button, highlighted when selected (same look as the inspector's quick actions).</summary>
    public static Element Choice(string label, bool selected, Action onClick, string automationName, bool enabled = true) =>
        Border(Button(label, onClick).SubtleButton().IsEnabled(enabled).AutomationName(automationName))
            .Selected(selected).CornerRadius(6);

    /// <summary>The current time, rounded to the minute so renders are stable.</summary>
    public static DateTimeOffset Now()
    {
        var t = DateTimeOffset.UtcNow;
        return new DateTimeOffset(t.UtcTicks - t.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }
}
