using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
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

    public static Element EmptyNote(string text) =>
        Muted(text).Margin(4, 8);

    public static Element ComingSoon(string title, string what) =>
        VStack(4, PageHeader(title, what),
            Card(VStack(8, BodyStrong("Coming soon"), Muted("This page is being built. See PLAN.md for the scope."))));
}
