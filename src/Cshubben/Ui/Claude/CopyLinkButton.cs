using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

/// <summary>What to copy: the link itself, or a Markdown link when <paramref name="MarkdownTitle"/> is given.</summary>
sealed record CopyLinkProps(string? Url, string Label = "Copy link", string? MarkdownTitle = null);

/// <summary>A small button that copies a link to the clipboard and says "Copied" for a moment.</summary>
sealed class CopyLinkButton : Component<CopyLinkProps>
{
    private const string GlyphCopy = "";
    private const string GlyphCheck = "";

    public static Element For(string? url, string label = "Copy link", string? markdownTitle = null) =>
        Component<CopyLinkButton, CopyLinkProps>(new CopyLinkProps(url, label, markdownTitle));

    public override Element Render()
    {
        var (copied, setCopied) = UseState(false);
        var announce = UseAnnounce();

        UseEffect(() =>
        {
            if (!copied) return () => { };
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1600, cts.Token);
                    setCopied(false);
                }
                catch (OperationCanceledException)
                {
                }
            });
            return () => cts.Cancel();
        }, new object[] { copied });

        var url = Props.Url;
        if (string.IsNullOrWhiteSpace(url)) return Empty();

        var text = Props.MarkdownTitle is { } title
            ? "[" + title.Replace("[", "(", StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal) + "](" + url + ")"
            : url;

        var button = Button(
                HStack(6, Kit.Glyph(copied ? GlyphCheck : GlyphCopy, 12), TextBlock(copied ? "Copied" : Props.Label).FontSize(12)),
                () =>
                {
                    if (!ExternalActions.Copy(text)) return;
                    setCopied(true);
                    announce.Announce("Copied");
                })
            .SubtleButton()
            .AutomationName(Props.Label);

        // The live region must be in the tree or the announcement is lost.
        return HStack(0, announce.Region, button);
    }
}
