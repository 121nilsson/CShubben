using System.Globalization;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

sealed record BriefingProps(HubSnapshot Snapshot, Action<string> OnSelect);

/// <summary>A generated briefing, kept for the session so leaving the Overview does not throw it away.</summary>
sealed record BriefingCache(BriefingResult Result, DateTimeOffset At, string Model, double CostUsd, bool Demo);

/// <summary>
/// "Today's briefing" at the top of the Overview. Never runs on its own: the Generate button asks Claude (Prompts.Briefing, parsed
/// by BriefingParser) and the result is cached. Demo mode starts with a ready-made demo briefing.
/// </summary>
sealed class BriefingCard : Component<BriefingProps>
{
    /// <summary>The last generated briefing. Null until one was generated (or, in demo mode, until the first render).</summary>
    public static BriefingCache? Cached { get; set; }

    public override Element Render()
    {
        var snap = Props.Snapshot;
        var onSelect = Props.OnSelect;
        var claude = IntegrationServices.Claude;

        var (running, setRunning) = UseState(false, true);
        var (error, setError) = UseState("", true);
        var (tick, setTick) = UseState(0, true);
        var (expanded, setExpanded) = UseState(false);

        if (Cached is null && claude.IsDemo)
            Cached = new BriefingCache(DemoClaudeData.Briefing(snap), snap.At, "demo", 0, true);
        var cache = Cached;

        async Task Generate()
        {
            setRunning(true);
            setError("");
            try
            {
                if (claude is DemoClaudeData)
                {
                    await Task.Delay(900);
                    Cached = new BriefingCache(DemoClaudeData.Briefing(snap), snap.At, "demo", 0, true);
                }
                else
                {
                    var result = await claude.RunAsync(ClaudeRequest.From(Prompts.Briefing(snap)) with { Feature = ClaudeFeature.Briefing }, CancellationToken.None);
                    if (!result.Success)
                    {
                        setError(result.Error == ClaudeErrorKind.AuthFailed ? ClaudeAuth.Hint : (result.ErrorMessage ?? "Claude could not write the briefing."));
                    }
                    else
                    {
                        var parsed = BriefingParser.Parse(result.Text);
                        if (parsed is null) setError("Claude's answer could not be read as a briefing. Try again.");
                        else Cached = new BriefingCache(parsed, DateTimeOffset.UtcNow, result.Model, result.CostUsd, false);
                    }
                }
            }
            catch (Exception ex)
            {
                setError(ex.Message);
            }

            setRunning(false);
            setTick(tick + 1);
        }

        void Open(string url)
        {
            var pr = snap.All.FirstOrDefault(p => p.Url == url);
            if (pr is not null) onSelect(pr.Id);
            else ExternalActions.OpenUrl(url);
        }

        var title = HStack(8, TextBlock("✦").Foreground(Theme.AccentText), BodyStrong("Today's briefing"));

        Element body;
        Element? action = null;
        if (!claude.IsAvailable)
        {
            body = VStack(6,
                BodyStrong("Claude Code is not installed"),
                Kit.Muted("Install Claude Code and sign in (claude auth login) to get a written briefing of what needs you today. "
                    + "Everything else on this page works without it.").TextWrapping());
        }
        else
        {
            action = Button(running ? "Working…" : cache is null ? "Generate" : "Regenerate", () => _ = Generate())
                .AccentButton().IsEnabled(!running).VAlign(VerticalAlignment.Top).AutomationName("Generate the briefing");

            Element content;
            if (cache is null)
            {
                content = running
                    ? Parts.Busy("Claude is reading your pull requests…")
                    : Kit.Muted("Ask Claude for a short briefing of what needs you today. It runs only when you press Generate.").TextWrapping();
            }
            else
            {
                content = Result(cache, snap.At, running, Open, expanded, () => setExpanded(!expanded));
            }

            body = VStack(10,
                content,
                error.Length > 0 ? Parts.Banner("No briefing", error, Tone.Danger) : null);
        }

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            title.VAlign(VerticalAlignment.Center).Grid(column: 0),
            (action ?? Empty()).Grid(column: 1));

        return Card(VStack(12, header, body));
    }

    private const int CompactItems = 3;

    private static Element Result(BriefingCache cache, DateTimeOffset now, bool running, Action<string> open, bool expanded, Action toggle)
    {
        var result = cache.Result;
        var age = Age.Format(cache.At, DateTimeOffset.UtcNow);
        var meta = new List<string>
        {
            cache.Demo ? "Demo briefing" : age == "now" ? "Generated just now" : "Generated " + age + " ago",
        };
        if (cache.Model.Length > 0 && !cache.Demo) meta.Add(cache.Model);
        if (cache.CostUsd > 0) meta.Add("$" + cache.CostUsd.ToString("0.000", CultureInfo.InvariantCulture));

        var total = result.Items.Count;
        var visible = expanded ? result.Items : result.Items.Take(CompactItems).ToList();
        var items = visible.Select((item, index) => ItemRow(item, index, open)).ToArray();
        Element? more = total > CompactItems
            ? Button(expanded ? "Show less" : "Show all " + total + " items", toggle).SubtleButton().HAlign(HorizontalAlignment.Left)
                .AutomationName(expanded ? "Show fewer briefing items" : "Show all briefing items")
            : null;

        var summary = result.Summary.Length > 0 ? Body(result.Summary).Foreground(Theme.SecondaryText).TextWrapping() : null;
        if (summary is not null && !expanded) summary = summary.MaxLines(2).TextTrimming(TextTrimming.CharacterEllipsis);
        return VStack(8,
            result.Headline.Length > 0 ? TextBlock(result.Headline).FontSize(16).SemiBold().TextWrapping() : null,
            summary,
            items.Length > 0 ? FlexColumn(items) with { RowGap = 2 } : null,
            more,
            running ? Parts.Busy("Writing a new briefing…") : null,
            Kit.Muted(string.Join(" · ", meta)));
    }

    private static Element ItemRow(BriefingItem item, int index, Action<string> open)
    {
        var (label, tone) = item.Kind switch
        {
            BriefingItemKind.Action => ("Do", Tone.Accent),
            BriefingItemKind.Watch => ("Watch", Tone.Warning),
            _ => ("Info", Tone.Neutral),
        };

        var content = Grid(
            columns: [GridSize.Px(56), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            item.Kind == BriefingItemKind.Action
                ? TextBlock("●").FontSize(8).Foreground(Theme.AccentText).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top).Margin(0, 5, 0, 0).Grid(column: 0)
                : Kit.Pill(label, tone).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Grid(column: 0),
            TextBlock(item.Text).TextWrapping().MaxLines(2).TextTrimming(TextTrimming.CharacterEllipsis).ToolTip(item.Text).Grid(column: 1),
            item.PrUrl is null ? Empty().Grid(column: 2) : Kit.Muted("↗").VAlign(VerticalAlignment.Top).Margin(8, 0, 0, 0).Grid(column: 2));

        if (item.PrUrl is not string url)
            return Border(content).Padding(12, 6).WithKey("item-" + index);

        return Border(Button(content, () => open(url))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .AutomationName(item.Text))
            .CornerRadius(6).WithKey("item-" + index);
    }
}
