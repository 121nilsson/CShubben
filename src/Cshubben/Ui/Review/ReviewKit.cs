using System.Security.Cryptography;
using System.Text;
using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

/// <summary>Small helpers shared by the review views: fonts, glyphs, text shaping and links.</summary>
static class ReviewKit
{
    public const string Mono = "Cascadia Mono, Consolas, Courier New";

    public const string GlyphChevronRight = "";
    public const string GlyphChevronDown = "";
    public const string GlyphFolder = "";
    public const string GlyphCheck = "";
    public const string GlyphAdd = "";
    public const string GlyphRemove = "";
    public const string GlyphEdit = "";
    public const string GlyphRename = "";
    public const string GlyphComment = "";
    public const string GlyphBack = "";
    public const string GlyphLink = "";
    public const string GlyphDelete = "";
    public const string GlyphFile = "";

    /// <summary>Tabs become four spaces and leading spaces become non-breaking so indentation survives wrapping.</summary>
    public static string CodeText(string text)
    {
        var expanded = text.Replace("\t", "    ", StringComparison.Ordinal);
        var lead = 0;
        while (lead < expanded.Length && expanded[lead] == ' ') lead++;
        var shaped = lead == 0 ? expanded : new string(' ', lead) + expanded.Substring(lead);
        return shaped.Length == 0 ? " " : shaped;
    }

    /// <summary>Makes the outer spaces of a highlighted piece visible; inner spaces stay breakable.</summary>
    public static string PieceText(string text)
    {
        var t = text.Replace("\t", "    ", StringComparison.Ordinal);
        if (t.Length == 0) return t;
        var start = 0;
        while (start < t.Length && t[start] == ' ') start++;
        var end = t.Length;
        while (end > start && t[end - 1] == ' ') end--;
        return new string(' ', start) + t.Substring(start, end - start) + new string(' ', t.Length - end);
    }

    public static Uri? SafeUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    /// <summary>GitHub's anchor for a file on the "Files changed" page: "diff-" and the SHA-256 of the path.</summary>
    public static string FileAnchor(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "diff-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FileUrl(ReviewTarget pr, string path) => pr.Url.TrimEnd('/') + "/files#" + FileAnchor(path);

    public static string TimeAgo(DateTimeOffset at) => Age.Format(at, DateTimeOffset.UtcNow) + " ago";

    public static string FirstLine(string text, int max = 90)
    {
        var line = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
        line = line.Trim();
        return line.Length <= max ? line : line.Substring(0, max - 1) + "…";
    }

    public static Element OpenOnGitHub(string? url, string label = "Open on GitHub")
    {
        var uri = SafeUri(url);
        if (uri is null) return Empty();
        return FlexRow(
            HyperlinkButton(HStack(6, Kit.Glyph(GlyphLink, 12), TextBlock(label).FontSize(12)), uri).AutomationName(label),
            Cshubben.Ui.Assist.CopyLinkButton.For(uri.AbsoluteUri)) with { ColumnGap = 2 };
    }

    public static Element Avatar(string author, double size = 24)
    {
        var initial = author.Length == 0 ? "?" : author.Substring(0, 1).ToUpperInvariant();
        return Border(TextBlock(initial).FontSize(size * 0.5).SemiBold().Foreground(Theme.AccentText)
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
            .Width(size).Height(size).CornerRadius(size / 2).Background(Theme.SystemAttentionBackground);
    }

    public static (string Glyph, Tone Tone) StatusIcon(DiffFileStatus status) => status switch
    {
        DiffFileStatus.Added => (GlyphAdd, Tone.Success),
        DiffFileStatus.Deleted => (GlyphRemove, Tone.Danger),
        DiffFileStatus.Renamed => (GlyphRename, Tone.Accent),
        _ => (GlyphEdit, Tone.Neutral),
    };

    public static string StatusLabel(DiffFileStatus status) => status switch
    {
        DiffFileStatus.Added => "Added",
        DiffFileStatus.Deleted => "Deleted",
        DiffFileStatus.Renamed => "Renamed",
        _ => "Modified",
    };

    /// <summary>"+12 −3" with colour only on the numbers that are not zero.</summary>
    public static Element Counts(int additions, int deletions, double size = 11) =>
        HStack(6,
            TextBlock("+" + additions).FontSize(size).Foreground(additions > 0 ? Kit.Fg(Tone.Success) : Theme.SecondaryText),
            TextBlock("−" + deletions).FontSize(size).Foreground(deletions > 0 ? Kit.Fg(Tone.Danger) : Theme.SecondaryText));

    /// <summary>A thin bar showing the share of additions against deletions.</summary>
    public static Element ChangeBar(int additions, int deletions, double width = 48)
    {
        var total = additions + deletions;
        if (total == 0) return Border(Empty()).Width(width).Height(4).CornerRadius(2).Background(Theme.SubtleFill);
        var bar = Grid(
            columns: [GridSize.Star(Math.Max(additions, 0.0001)), GridSize.Star(Math.Max(deletions, 0.0001))],
            rows: [GridSize.Auto],
            Border(Empty()).Height(4).Background(Theme.SystemSuccess).Grid(column: 0),
            Border(Empty()).Height(4).Background(Theme.SystemCritical).Grid(column: 1));
        return Border(bar).Width(width).CornerRadius(2);
    }

    public static Element ProgressBar(double fraction, double height = 4)
    {
        var done = Math.Clamp(fraction, 0.0, 1.0);
        var bar = Grid(
            columns: [GridSize.Star(Math.Max(done, 0.0001)), GridSize.Star(Math.Max(1.0 - done, 0.0001))],
            rows: [GridSize.Auto],
            Border(Empty()).Height(height).Background(done >= 1.0 ? Theme.SystemSuccess : Theme.AccentText).Grid(column: 0),
            Border(Empty()).Height(height).Background(Theme.SubtleFill).Grid(column: 1));
        return Border(bar).CornerRadius(height / 2);
    }

    public static Element Surface(Element content) =>
        Border(content).Background(Theme.CardBackground).WithBorder(Theme.ControlStroke).CornerRadius(8).Padding(12);
}
