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
    /// so the chart never overflows a narrow pane. Empty buckets are a faint 2 px line.
    /// </summary>
    public static Element Bars(IReadOnlyList<int> counts, int buckets, double height, Tone tone, int? scaleMax = null)
    {
        var tail = ErrorChart.Tail(counts, buckets);
        var heights = ErrorChart.BarHeights(tail, height, 2, scaleMax);
        var columns = new GridSize[heights.Count];
        var bars = new Element[heights.Count];
        for (var i = 0; i < bars.Length; i++)
        {
            columns[i] = GridSize.Star();
            var bar = heights[i] <= 0
                ? Border(Empty()).Height(2).Background(Theme.SecondaryText).Opacity(0.3)
                : Border(Empty()).Height(heights[i]).Background(Kit.Fg(tone));
            bars[i] = bar.VAlign(VerticalAlignment.Bottom).Margin(0, 0, 1, 0).Grid(column: i);
        }

        return Grid(columns: columns, rows: [GridSize.Star()], bars).Height(height);
    }

    /// <summary>The highest bucket of the last <paramref name="buckets"/> buckets.</summary>
    public static int Peak(IReadOnlyList<int> counts, int buckets)
    {
        var tail = ErrorChart.Tail(counts, buckets);
        return tail.Count == 0 ? 0 : tail.Max();
    }
}
