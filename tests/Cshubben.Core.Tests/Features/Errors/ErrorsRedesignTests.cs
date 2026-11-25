using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class SpikeLevelTests
{
    [Theory]
    [InlineData(2.3, SpikeLevel.Mild)]
    [InlineData(2.99, SpikeLevel.Mild)]
    [InlineData(3.0, SpikeLevel.Elevated)]
    [InlineData(4.9, SpikeLevel.Elevated)]
    [InlineData(5.0, SpikeLevel.Severe)]
    [InlineData(13, SpikeLevel.Severe)]
    public void Level_follows_the_multiplier(double factor, SpikeLevel expected) =>
        Assert.Equal(expected, SpikeLevels.Of(factor));

    [Fact]
    public void A_bigger_spike_has_a_bigger_multiplier()
    {
        Assert.True(SpikeLevels.FontSize(SpikeLevel.Severe) > SpikeLevels.FontSize(SpikeLevel.Elevated));
        Assert.True(SpikeLevels.FontSize(SpikeLevel.Elevated) > SpikeLevels.FontSize(SpikeLevel.Mild));
    }
}

public class TailIndexTests
{
    [Theory]
    [InlineData(287, 288, 72, 71)] // the newest bucket is the last bar
    [InlineData(216, 288, 72, 0)] // the first bucket of the tail
    [InlineData(215, 288, 72, -1)] // just before the tail
    [InlineData(10, 50, 72, 32)] // a short series is padded at the start (22 zeros)
    [InlineData(-1, 288, 72, -1)]
    [InlineData(288, 288, 72, -1)]
    public void Tail_index_matches_the_padded_tail(int index, int count, int length, int expected) =>
        Assert.Equal(expected, ErrorChart.TailIndex(index, count, length));
}
