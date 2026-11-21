using Cshubben.Core.Features.Stats;
using Xunit;

namespace Cshubben.Core.Tests.Features.Stats;

public class StatsExtrasTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero); // Saturday

    private static MergedPrRecord Pr(string repo, int n, DateTimeOffset merged, double cycleHours = 5, int add = 10, int del = 2) =>
        new(repo, n, "t" + n, "me", merged.AddHours(-cycleHours), merged, null, add, del, 1);

    [Fact]
    public void Extras_count_reviews_weekdays_churn_and_slowest()
    {
        var input = new StatsInput(
            new[]
            {
                Pr("a/x", 1, new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), 30),
                Pr("a/x", 2, new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), 2),
                Pr("a/y", 3, new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), 90, 100, 50),
            },
            new[]
            {
                new ReviewRecord("a/x", 9, Now), new ReviewRecord("a/x", 9, Now), new ReviewRecord("a/x", 10, Now), new ReviewRecord("a/y", 1, Now),
            });
        var e = StatsExtrasCalculator.Compute(input, Now);

        Assert.Equal(("a/x", 2), (e.ReviewedRepos[0].Repo, e.ReviewedRepos[0].Count));
        Assert.Equal(1, e.Weekdays.Single(d => d.Day == DayOfWeek.Monday).Count);
        Assert.Equal(2, e.Weekdays.Single(d => d.Day == DayOfWeek.Tuesday).Count);
        Assert.Equal(DayOfWeek.Monday, e.Weekdays[0].Day);
        Assert.Equal(3, e.Slowest[0].Number);
        Assert.Equal(120, e.LinesAdded);
        Assert.Equal(54, e.LinesDeleted);
    }

    [Fact]
    public void Streak_counts_back_and_forgives_an_empty_current_week()
    {
        var thisWeek = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        var merged = new[] { Pr("a/x", 1, thisWeek), Pr("a/x", 2, thisWeek.AddDays(-7)), Pr("a/x", 3, thisWeek.AddDays(-21)) };
        Assert.Equal(2, StatsExtrasCalculator.Streak(merged, Now));
        Assert.Equal(2, StatsExtrasCalculator.Streak(merged, Now.AddDays(7)));
        Assert.Equal(0, StatsExtrasCalculator.Streak(Array.Empty<MergedPrRecord>(), Now));
    }

    [Fact]
    public void Team_ranks_people_and_repos_and_skips_bots_as_people()
    {
        var t = Now;
        var prs = new[]
        {
            new TeamPr("a/x", "anna", t, 100), new TeamPr("a/x", "Anna", t, 50), new TeamPr("a/y", "bo", t, 500),
            new TeamPr("a/y", "dependabot[bot]", t, 5), new TeamPr("a/z", "", t, 1),
        };
        var r = TeamStats.Compute(prs);
        Assert.Equal(5, r.Merged);
        Assert.Equal(2, r.People);
        Assert.Equal(("anna", 2, 150L), (r.TopPeople[0].Author, r.TopPeople[0].Merged, r.TopPeople[0].Lines));
        Assert.Equal("bo", r.TopPeople[1].Author);
        Assert.Equal(("a/x", 2), (r.TopRepos[0].Repo, r.TopRepos[0].Count));
        Assert.Equal(2, TeamStats.Compute(prs, take: 2).TopRepos.Count);
    }
}
