using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>Small reusable building blocks. Colours always come from theme tokens, never hex.</summary>
static class Kit
{
    public static ThemeRef Fg(Tone tone) => tone switch
    {
        Tone.Danger => Theme.SystemCritical,
        Tone.Warning => Theme.SystemCaution,
        Tone.Success => Theme.SystemSuccess,
        Tone.Accent => Theme.AccentText,
        _ => Theme.SecondaryText,
    };

    public static ThemeRef Bg(Tone tone) => tone switch
    {
        Tone.Danger => Theme.SystemCriticalBackground,
        Tone.Warning => Theme.SystemCautionBackground,
        Tone.Success => Theme.SystemSuccessBackground,
        Tone.Accent => Theme.SystemAttentionBackground,
        _ => Theme.SystemNeutralBackground,
    };

    /// <summary>Highlights an element as the selected one.</summary>
    public static T Selected<T>(this T element, bool on) where T : Element =>
        on ? element.Background(Theme.SubtleFill) : element;

    /// <summary>An icon from the Segoe Fluent Icons font, as a plain text element. Decorative: the owning control must carry the AutomationName.</summary>
    public static TextBlockElement Glyph(string glyph, double size = 16) =>
        TextBlock(glyph).FontFamily("Segoe Fluent Icons").FontSize(size)
            .AccessibilityView(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw); // decorative, hidden from Narrator

    /// <summary>Markdown with calmer headings than the framework default (28 px h1 is too loud inside a panel).</summary>
    public static Element Md(string markdown) =>
        Microsoft.UI.Reactor.Advanced.Factories.Markdown(markdown, MdOptions);

    private static readonly Microsoft.UI.Reactor.Markdown.MarkdownOptions MdOptions = new()
    {
        Heading = (level, element) => element is RichTextBlockElement rich
            ? rich.FontSize(level switch { 1 => 18, 2 => 16, 3 => 14, _ => 13 })
            : element,
    };

    public static TextBlockElement Muted(string text) =>
        TextBlock(text).FontSize(12).Foreground(Theme.SecondaryText);

    public static Element Pill(string text, Tone tone) =>
        Border(TextBlock(text).FontSize(12).SemiBold().Foreground(Fg(tone)))
            .Background(Bg(tone)).CornerRadius(10).Padding(8, 2);

    public static Element SectionLabel(string text) =>
        TextBlock(text.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Theme.SecondaryText).Margin(12, 16, 0, 4);

    public static Element PageHeader(string title, string subtitle) =>
        VStack(2, Title(title), Muted(subtitle)).Margin(0, 0, 0, 16);

    public static Element Heading2(string text, int? count = null) =>
        HStack(8,
            BodyStrong(text),
            count is null ? null : Pill(count.Value.ToString(), Tone.Neutral));

    /// <summary>Caps page content width on large windows. Left-aligned, so every page title sits at the same x.</summary>
    public static Element Capped(Element content, double max = 1100) =>
        Grid(
            columns: [GridSize.Star(1000).MaxSize(max), GridSize.Star(0.001)],
            rows: [GridSize.Star()],
            content.Grid(column: 0));

    public static Element EmptyNote(string text) =>
        Muted(text).Margin(4, 8);

    public static Element ComingSoon(string title, string what) =>
        VStack(4, PageHeader(title, what),
            Card(VStack(8, BodyStrong("Coming soon"), Muted("This page is being built. See PLAN.md for the scope."))));

    /// <summary>
    /// A calm full-width message strip with an optional action button (for example "Retry").
    /// Colour comes from the tone's theme tokens.
    /// </summary>
    public static Element Banner(Tone tone, string title, string message, string? actionLabel = null, Action? onAction = null) =>
        Border(Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                VStack(2,
                    BodyStrong(title),
                    TextBlock(message).FontSize(12).Foreground(Theme.SecondaryText).TextWrapping()).Grid(column: 0),
                actionLabel is null || onAction is null
                    ? Empty()
                    : Button(actionLabel, onAction).VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).Grid(column: 1)))
            .Background(Bg(tone)).CornerRadius(8).Padding(14, 10);

    /// <summary>First-load state: a spinner, a sentence, and grey placeholder rows so the page does not jump.</summary>
    public static Element LoadingState(string message, int rows = 4)
    {
        var children = new List<Element>
        {
            HStack(12,
                ProgressRing().IsActive().Width(20).Height(20).VAlign(VerticalAlignment.Center),
                BodyStrong(message).VAlign(VerticalAlignment.Center)).Margin(0, 0, 0, 8),
        };
        for (var i = 0; i < rows; i++)
            children.Add(Border(Empty()).Height(56).CornerRadius(8).Background(Theme.SubtleFill).Opacity(0.6));
        return ScrollView(Capped(VStack(12, children.ToArray()).Margin(24, 16)));
    }

    /// <summary>A titled card used by the Settings page: heading, optional one-line help, then the content.</summary>
    public static Element SettingsCard(string title, string? help, params Element[] content) =>
        Card(VStack(12,
            VStack(2, BodyStrong(title), help is null ? Empty() : Muted(help).TextWrapping()),
            VStack(10, content)));
}
