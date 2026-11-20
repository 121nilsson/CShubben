using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Settings;

public class FreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Under_a_minute_is_just_now() =>
        Assert.Equal("Updated just now", Freshness.Label(Now.AddSeconds(-20), Now));

    [Fact]
    public void Future_time_is_just_now() =>
        Assert.Equal("Updated just now", Freshness.Label(Now.AddMinutes(3), Now));

    [Fact]
    public void Minutes_and_hours_use_the_age_format()
    {
        Assert.Equal("Updated 2 min ago", Freshness.Label(Now.AddMinutes(-2), Now));
        Assert.Equal("Updated 3 h ago", Freshness.Label(Now.AddHours(-3), Now));
    }
}
