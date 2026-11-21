using Cshubben.Core.Features.Updates;
using Xunit;

namespace Cshubben.Core.Tests.Features.Updates;

public class UpdateTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Available_names_the_version()
    {
        var c = new UpdateCheck(UpdateStatus.Available, Now, "1.2.3");
        Assert.Equal("Version 1.2.3 is available.", UpdateText.Status(c));
    }

    [Fact]
    public void Failed_includes_the_error()
    {
        var c = new UpdateCheck(UpdateStatus.Failed, Now, Error: "offline");
        Assert.Equal("Could not check for updates. offline", UpdateText.Status(c));
    }

    [Fact]
    public void No_check_gives_no_text() => Assert.Equal("", UpdateText.Status(null));

    [Fact]
    public void Last_checked_is_relative_then_absolute()
    {
        Assert.Equal("Never", UpdateText.LastChecked(null, Now));
        Assert.Equal("Just now", UpdateText.LastChecked(Now.AddSeconds(-5), Now));
        Assert.Equal("5 min ago", UpdateText.LastChecked(Now.AddMinutes(-5), Now));
        Assert.Equal("3 h ago", UpdateText.LastChecked(Now.AddHours(-3), Now));
    }
}
