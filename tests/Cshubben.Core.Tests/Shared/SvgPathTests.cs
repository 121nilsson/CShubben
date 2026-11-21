using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Shared;

public class SvgPathTests
{
    [Fact]
    public void Reads_absolute_lines_and_closes()
    {
        var f = Assert.Single(SvgPath.Parse("M1 2 L3 4 H5 V6 Z"));
        Assert.Equal(new SvgPoint(1, 2), f.Start);
        Assert.True(f.Closed);
        Assert.Equal(new[] { new SvgPoint(3, 4), new SvgPoint(5, 4), new SvgPoint(5, 6) }, f.Segments.Select(s => s.To).ToArray());
    }

    [Fact]
    public void Relative_commands_and_implicit_lineto_after_move()
    {
        var f = Assert.Single(SvgPath.Parse("m1 1 2 3 l1 1h2v-1"));
        Assert.Equal(new[] { new SvgPoint(3, 4), new SvgPoint(4, 5), new SvgPoint(6, 5), new SvgPoint(6, 4) }, f.Segments.Select(s => s.To).ToArray());
    }

    [Fact]
    public void Numbers_written_back_to_back_are_split()
    {
        var f = Assert.Single(SvgPath.Parse("M0 0L9.573.677.5-.25"));
        Assert.Equal(new[] { new SvgPoint(9.573, 0.677), new SvgPoint(0.5, -0.25) }, f.Segments.Select(s => s.To).ToArray());
    }

    [Fact]
    public void Arc_flags_and_endpoint_are_read_relative()
    {
        var f = Assert.Single(SvgPath.Parse("M1 1a2 3 0 1 1 4 5"));
        var arc = Assert.Single(f.Segments);
        Assert.Equal(SvgSegmentKind.Arc, arc.Kind);
        Assert.Equal((2, 3), (arc.RadiusX, arc.RadiusY));
        Assert.True(arc.LargeArc);
        Assert.True(arc.Sweep);
        Assert.Equal(new SvgPoint(5, 6), arc.To);
    }

    [Fact]
    public void Each_move_starts_a_new_figure_and_a_close_returns_to_the_start()
    {
        var figures = SvgPath.Parse("M0 0 L2 0 L2 2Z m1 1 l1 0 l0 1Z");
        Assert.Equal(2, figures.Count);
        Assert.Equal(new SvgPoint(1, 1), figures[1].Start);
    }

    [Fact]
    public void Rejects_unsupported_commands_and_missing_numbers()
    {
        Assert.Throws<FormatException>(() => SvgPath.Parse("M0 0 Q1 1 2 2"));
        Assert.Throws<FormatException>(() => SvgPath.Parse("M0 0 L1"));
    }
}
