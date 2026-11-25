using Cshubben.Core.Features.Errors;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>Drawing helpers of the Errors page. The sizes come from <see cref="ErrorChart"/> (tested); this only draws rectangles.</summary>
static class ErrorsKit
{
    /// <summary>
    /// A row of thin bars, one per bucket, growing from the bottom. The bars share the width they are given (one star column each),
    /// so the chart never overflows a narrow pane. Empty buckets are a faint 2 px line. <paramref name="markers"/> are the buckets
    /// (counted in the chart, see <see cref="ErrorMarkers"/>) where a deployment happened; each gets a thin accent line.
    /// </summary>
    public static Element Bars(IReadOnlyList<int> counts, int buckets, double height, Tone tone, int? scaleMax = null, IReadOnlyCollection<int>? markers = null, int? shadeFrom = null)
    {
        var tail = ErrorChart.Tail(counts, buckets);
        var heights = ErrorChart.BarHeights(tail, height, 2, scaleMax);
        var columns = new GridSize[heights.Count];
        var bars = new List<Element>(heights.Count + (markers?.Count ?? 0) + heights.Count);
        for (var i = 0; i < heights.Count; i++)
        {
            columns[i] = GridSize.Star();
            // A faint wash behind the buckets from the shaded one on (when a spike started), drawn first so the bars sit on top.
            if (shadeFrom is { } from && i >= from)
                bars.Add(Border(Empty()).Background(Kit.Bg(Tone.Danger)).Opacity(0.6).Grid(column: i));
        }

        for (var i = 0; i < heights.Count; i++)
        {
            var bar = heights[i] <= 0
                ? Border(Empty()).Height(2).Background(Theme.SecondaryText).Opacity(0.3)
                : Border(Empty()).Height(heights[i]).Background(Kit.Fg(tone));
            bars.Add(bar.VAlign(VerticalAlignment.Bottom).Margin(0, 0, 1, 0).Grid(column: i));
        }

        // Added last, so they are drawn on top of the bars.
        if (markers is not null)
        {
            foreach (var marker in markers)
            {
                if (marker < 0 || marker >= heights.Count) continue;
                bars.Add(Border(Empty()).Width(2).Background(Theme.Accent).HAlign(HorizontalAlignment.Left).Grid(column: marker));
            }
        }

        return Grid(columns: columns, rows: [GridSize.Star()], bars.ToArray()).Height(height);
    }

    /// <summary>Bars with faint lines at the peak and half of it, and the peak written in the corner, so the height means something.</summary>
    public static Element GridBars(IReadOnlyList<int> counts, int buckets, double height, Tone tone, IReadOnlyCollection<int>? markers = null, int? shadeFrom = null)
    {
        var peak = Peak(counts, buckets);
        Element line(VerticalAlignment at) =>
            Border(Empty()).Height(1).Background(Theme.SecondaryText).Opacity(0.18).VAlign(at);

        var chart = Bars(counts, buckets, height, tone, null, markers, shadeFrom);
        if (peak <= 0) return chart;

        return Grid(
            columns: [GridSize.Star()],
            rows: [GridSize.Star()],
            line(VerticalAlignment.Top),
            line(VerticalAlignment.Center),
            chart,
            TextBlock(peak.ToString()).FontSize(11).Foreground(Theme.SecondaryText).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Margin(2, 2, 0, 0)).Height(height);
    }

    /// <summary>Two time labels under a zoomed chart: when the slice starts and ends (local clock, with the date when it is not today).</summary>
    public static Element RangeAxis(DateTimeOffset from, DateTimeOffset to)
    {
        static string Label(DateTimeOffset t)
        {
            var local = t.ToLocalTime();
            return local.Date == DateTime.Now.Date ? local.ToString("HH:mm") : local.ToString("MMM d HH:mm");
        }

        return Grid(
            columns: [GridSize.Star(), GridSize.Star()],
            rows: [GridSize.Auto],
            Kit.Muted(Label(from)).Grid(column: 0),
            Kit.Muted(Label(to)).HAlign(HorizontalAlignment.Right).Grid(column: 1));
    }

    /// <summary>Clock times (local) under a chart of the last <paramref name="buckets"/> buckets: five evenly spaced labels, the last one "now".</summary>
    public static Element TimeAxis(ErrorWindow window, int buckets)
    {
        var shown = Math.Min(buckets, window.Buckets);
        var from = window.End - TimeSpan.FromTicks(window.Step.Ticks * shown);
        const int labels = 5;
        var columns = new GridSize[labels];
        var items = new Element[labels];
        for (var i = 0; i < labels; i++)
        {
            columns[i] = GridSize.Star();
            var at = from + TimeSpan.FromTicks((window.End - from).Ticks * i / (labels - 1));
            var text = i == labels - 1 ? "now" : at.ToLocalTime().ToString("HH:mm");
            var label = Kit.Muted(text);
            items[i] = (i == 0 ? label : i == labels - 1 ? label.HAlign(HorizontalAlignment.Right) : label.HAlign(HorizontalAlignment.Center)).Grid(column: i);
        }

        return Grid(columns: columns, rows: [GridSize.Auto], items);
    }

    /// <summary>The highest bucket of the last <paramref name="buckets"/> buckets.</summary>
    public static int Peak(IReadOnlyList<int> counts, int buckets)
    {
        var tail = ErrorChart.Tail(counts, buckets);
        return tail.Count == 0 ? 0 : tail.Max();
    }
}
