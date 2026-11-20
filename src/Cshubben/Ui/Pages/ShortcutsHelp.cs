using Cshubben.Core.Features.CommandPalette;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>The keyboard shortcuts sheet, an overlay with the same look as the review help. Content comes from <see cref="ShortcutCatalog"/>.</summary>
static class ShortcutsHelp
{
    public static Element Build(Action close)
    {
        var sections = new List<Element>();
        foreach (var group in ShortcutCatalog.Groups)
        {
            var rows = ShortcutCatalog.All.Where(e => e.Group == group).Select(e => (Element)Grid(
                columns: [GridSize.Px(120), GridSize.Star()],
                rows: [GridSize.Auto],
                TextBlock(e.Keys).FontFamily("Consolas").SemiBold().Grid(column: 0),
                TextBlock(e.What).TextWrapping().Grid(column: 1))).ToArray();
            sections.Add(VStack(6, Kit.SectionLabel(group).Margin(0, 4, 0, 0), VStack(6, rows)));
        }

        var card = Border(FlexColumn(
                BodyStrong("Keyboard shortcuts"),
                ScrollView(VStack(8, sections.ToArray())).Flex(grow: 1, basis: 0),
                Kit.Muted("Single-key shortcuts pause while you type in a text box."),
                Button("Close", close).AccentButton().HAlign(HorizontalAlignment.Right).AutomationName("Close keyboard shortcuts")) with { RowGap = 12 })
            .Background(Theme.CardBackground).WithBorder(Theme.ControlStroke).CornerRadius(8).Padding(16);

        return Border(Border(card).MaxWidth(520).MaxHeight(620).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Margin(24))
            .Background(Theme.SmokeFill)
            .OnTapped((s, e) => close());
    }
}
