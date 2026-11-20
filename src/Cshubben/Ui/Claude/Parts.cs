using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

/// <summary>Small building blocks shared by the integration panels.</summary>
static class Parts
{
    public static Element Busy(string text) =>
        HStack(8,
            ProgressRing().Width(16).Height(16),
            Kit.Muted(text).VAlign(VerticalAlignment.Center));

    /// <summary>A tinted message box. Spend colour only on errors and warnings.</summary>
    public static Element Banner(string title, string message, Tone tone) =>
        Border(VStack(4,
                TextBlock(title).SemiBold().Foreground(Kit.Fg(tone)),
                Kit.Muted(message).TextWrapping()))
            .Background(Kit.Bg(tone)).CornerRadius(8).Padding(12);

    public static Element Label(string text) =>
        TextBlock(text.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Theme.SecondaryText);

    /// <summary>Value of a finished (or reloading) resource, else the default.</summary>
    public static T? Peek<T>(AsyncValue<T> value) => value switch
    {
        AsyncValue<T>.Data d => d.Value,
        AsyncValue<T>.Reloading r => r.Previous,
        _ => default,
    };

    public static bool IsLoading<T>(AsyncValue<T> value) => value is AsyncValue<T>.Loading;

    public static string? ErrorOf<T>(AsyncValue<T> value) => value is AsyncValue<T>.Error e ? e.Exception.Message : null;
}
