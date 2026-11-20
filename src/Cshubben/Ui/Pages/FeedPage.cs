using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

static class FeedPage
{
    public static Element Build(HubSnapshot snap)
    {
        var days = snap.Feed
            .OrderByDescending(i => i.At)
            .GroupBy(i => DayLabel(i.At, snap.At));

        var sections = days.Select(g =>
            Card(FlexColumn(
                Kit.Heading2(g.Key, g.Count()).Margin(0, 0, 0, 4),
                FlexColumn(g.Select(i => Rows.FeedRow(i, snap.At)).ToArray()))).Margin(0, 0, 0, 12));

        return ScrollView(
            FlexColumn(new[] { Kit.PageHeader("Feed", "What is happening in your repos") }.Concat(sections).ToArray())
                .FlexPadding(24, 16));
    }

    private static string DayLabel(DateTimeOffset at, DateTimeOffset now) =>
        (now.Date - at.Date).Days switch { 0 => "Today", 1 => "Yesterday", _ => at.ToString("dddd d MMM") };
}
