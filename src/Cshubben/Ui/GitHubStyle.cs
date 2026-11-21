using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>The git icons people know from GitHub (Octicons, 16 px).</summary>
enum GitIcon
{
    Merge,
    PullRequest,
    PullRequestClosed,
    Commit,
    Branch,
    Comment,
    Check,
    Cross,
}

/// <summary>
/// GitHub's own colours and icons: purple merged, green open, red closed or failed, yellow attention, grey neutral.
/// Primer palette, one set for dark and one for light. Used for the git meaning of things (merge, pull request, CI),
/// everything else keeps the theme tokens in <see cref="Kit"/>.
/// </summary>
static class GitHubStyle
{
    /// <summary>True when the app currently shows its dark theme (the Settings choice, or the system theme for "system").</summary>
    public static bool IsDark()
    {
        try
        {
            return ThemeChoice.Normalize(AppServices.Current.Settings.Theme) switch
            {
                ThemeChoice.Dark => true,
                ThemeChoice.Light => false,
                _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
            };
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static Color Rgb(int r, int g, int b, byte a = 255) => Color.FromArgb(a, (byte)r, (byte)g, (byte)b);

    public static Color ColorOf(Tone tone) => IsDark()
        ? tone switch
        {
            Tone.Merged => Rgb(0xA3, 0x71, 0xF7),
            Tone.Success => Rgb(0x3F, 0xB9, 0x50),
            Tone.Danger => Rgb(0xF8, 0x51, 0x49),
            Tone.Warning => Rgb(0xD2, 0x99, 0x22),
            Tone.Accent => Rgb(0x58, 0xA6, 0xFF),
            _ => Rgb(0x8B, 0x94, 0x9E),
        }
        : tone switch
        {
            Tone.Merged => Rgb(0x82, 0x50, 0xDF),
            Tone.Success => Rgb(0x1A, 0x7F, 0x37),
            Tone.Danger => Rgb(0xCF, 0x22, 0x2E),
            Tone.Warning => Rgb(0x9A, 0x67, 0x00),
            Tone.Accent => Rgb(0x09, 0x69, 0xDA),
            _ => Rgb(0x65, 0x6D, 0x76),
        };

    public static Brush Fg(Tone tone) => new SolidColorBrush(ColorOf(tone));

    /// <summary>Soft background for a pill: the colour at about 15 % opacity.</summary>
    public static Brush Bg(Tone tone)
    {
        var c = ColorOf(tone);
        return new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
    }

    private static string Data(GitIcon icon) => icon switch
    {
        GitIcon.Merge =>
            "M5.45 5.154A4.25 4.25 0 0 0 9.25 7.5h1.378a2.251 2.251 0 1 1 0 1.5H9.25A5.734 5.734 0 0 1 5 7.123v3.505a2.25 2.25 0 1 1-1.5 0V5.372a2.25 2.25 0 1 1 1.95-.218ZM4.25 13.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Zm8.5-4.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5ZM5 3.25a.75.75 0 1 0 0 .005V3.25Z",
        GitIcon.PullRequest =>
            "M1.5 3.25a2.25 2.25 0 1 1 3 2.122v5.256a2.251 2.251 0 1 1-1.5 0V5.372A2.25 2.25 0 0 1 1.5 3.25Zm5.677-.177L9.573.677A.25.25 0 0 1 10 .854V2.5h1A2.5 2.5 0 0 1 13.5 5v5.628a2.251 2.251 0 1 1-1.5 0V5a1 1 0 0 0-1-1h-1v1.646a.25.25 0 0 1-.427.177L7.177 3.427a.25.25 0 0 1 0-.354ZM3.75 2.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm0 9.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm8.25.75a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0Z",
        GitIcon.PullRequestClosed =>
            "M3.25 1A2.25 2.25 0 0 1 4 5.372v5.256a2.251 2.251 0 1 1-1.5 0V5.372A2.251 2.251 0 0 1 3.25 1Zm9.5 5.5a.75.75 0 0 1 .75.75v3.378a2.251 2.251 0 1 1-1.5 0V7.25a.75.75 0 0 1 .75-.75Zm-2.03-5.273a.75.75 0 0 1 1.06 0l.97.97.97-.97a.748.748 0 0 1 1.265.332.75.75 0 0 1-.205.729l-.97.97.97.97a.751.751 0 0 1-.018 1.042.751.751 0 0 1-1.042.018l-.97-.97-.97.97a.749.749 0 0 1-1.275-.326.749.749 0 0 1 .215-.734l.97-.97-.97-.97a.75.75 0 0 1 0-1.06ZM2.5 3.25a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0ZM3.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm9.5 0a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Z",
        GitIcon.Commit =>
            "M11.93 8.5a4.002 4.002 0 0 1-7.86 0H.75a.75.75 0 0 1 0-1.5h3.32a4.002 4.002 0 0 1 7.86 0h3.32a.75.75 0 0 1 0 1.5Zm-1.43-.75a2.5 2.5 0 1 0-5 0 2.5 2.5 0 0 0 5 0Z",
        GitIcon.Branch =>
            "M9.5 3.25a2.25 2.25 0 1 1 3 2.122V6A2.5 2.5 0 0 1 10 8.5H6a1 1 0 0 0-1 1v1.128a2.251 2.251 0 1 1-1.5 0V5.372a2.25 2.25 0 1 1 1.5 0v1.836A2.493 2.493 0 0 1 6 7h4a1 1 0 0 0 1-1v-.628A2.25 2.25 0 0 1 9.5 3.25Zm-6 0a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0Zm8.25-.75a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5ZM4.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Z",
        GitIcon.Comment =>
            "M1 2.75C1 1.784 1.784 1 2.75 1h10.5c.966 0 1.75.784 1.75 1.75v7.5A1.75 1.75 0 0 1 13.25 12H9.06l-2.573 2.573A1.458 1.458 0 0 1 4 13.543V12H2.75A1.75 1.75 0 0 1 1 10.25Zm1.75-.25a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h2a.75.75 0 0 1 .75.75v2.19l2.72-2.72a.749.749 0 0 1 .53-.22h4.5a.25.25 0 0 0 .25-.25v-7.5a.25.25 0 0 0-.25-.25Z",
        GitIcon.Check =>
            "M13.78 4.22a.75.75 0 0 1 0 1.06l-7.25 7.25a.75.75 0 0 1-1.06 0L2.22 9.28a.751.751 0 0 1 .018-1.042.751.751 0 0 1 1.042-.018L6 10.94l6.72-6.72a.75.75 0 0 1 1.06 0Z",
        _ =>
            "M3.72 3.72a.75.75 0 0 1 1.06 0L8 6.94l3.22-3.22a.749.749 0 0 1 1.275.326.749.749 0 0 1-.215.734L9.06 8l3.22 3.22a.749.749 0 0 1-.326 1.275.749.749 0 0 1-.734-.215L8 9.06l-3.22 3.22a.751.751 0 0 1-1.042-.018.751.751 0 0 1-.018-1.042L6.94 8 3.72 4.78a.75.75 0 0 1 0-1.06Z",
    };

    private static readonly System.Text.RegularExpressions.Regex PathToken =
        new(@"[A-Za-z]|-?(?:\d+\.\d*|\.\d+|\d+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly Dictionary<GitIcon, string> Normalized = new();

    /// <summary>
    /// Octicons write numbers back to back ("9.573.677", ".75.75"). Reactor's path parser reads those wrongly,
    /// so every command and number gets its own space.
    /// </summary>
    private static string Spaced(GitIcon icon)
    {
        lock (Normalized)
        {
            if (!Normalized.TryGetValue(icon, out var text))
                Normalized[icon] = text = string.Join(" ", PathToken.Matches(Data(icon)).Select(m => m.Value));
            return text;
        }
    }

    /// <summary>A git icon in a GitHub colour. Decorative: the owning control must carry the AutomationName.</summary>
    public static Element Icon(GitIcon icon, Tone tone, double size = 16)
    {
        var path = Path2D() with { PathDataString = Spaced(icon), Fill = Fg(tone) };
        return Border(path).Width(size).Height(size).VAlign(VerticalAlignment.Center);
    }

    /// <summary>A text element in a GitHub colour (for "merged #12").</summary>
    public static TextBlockElement Text(string text, Tone tone, double size = 12) =>
        TextBlock(text).FontSize(size).Foreground(Fg(tone));
}
