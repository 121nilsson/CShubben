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
    /// <summary>A row of thin bars, one per bucket, growing from the bottom. Empty buckets are a faint line.</summary>
    public static Element Bars(IReadOnlyList<int> counts, int buckets, double height, double barWidth, Tone tone, int? scaleMax = null)
    {
        var tail = ErrorChart.Tail(counts, buckets);
        var heights = ErrorChart.BarHeights(tail, height, 2, scaleMax);
        var bars = new Element[heights.Count];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = heights[i] <= 0
                ? Border(Empty()).Width(barWidth).Height(1).Background(Theme.ControlStroke).VAlign(VerticalAlignment.Bottom)
                : Border(Empty()).Width(barWidth).Height(heights[i]).Background(Kit.Fg(tone)).VAlign(VerticalAlignment.Bottom);
        }

        return HStack(1, bars).Height(height);
    }

}
