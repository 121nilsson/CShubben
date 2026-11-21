namespace Cshubben.Core.Features.Stats;

public sealed record WeekdayCount(DayOfWeek Day, int Count);

/// <summary>More numbers from the same history as <see cref="StatsReport"/>: where reviews went, which weekdays PRs merge, the slowest PRs, churn and the merge streak.</summary>
public sealed record StatsExtras(
    IReadOnlyList<RepoCount> ReviewedRepos,
    IReadOnlyList<WeekdayCount> Weekdays,
    IReadOnlyList<MergedPrRecord> Slowest,
    int StreakWeeks,
    long LinesAdded,
    long LinesDeleted);

public static class StatsExtrasCalculator
{
    public static StatsExtras Compute(StatsInput input, DateTimeOffset now, int take = 5)
    {
        ArgumentNullException.ThrowIfNull(input);
        var merged = input.Merged.Where(m => m.MergedAt <= now).ToList();

        var reviewed = input.Reviews
            .GroupBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RepoCount(g.Key, g.Select(r => r.Number).Distinct().Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)
            .Take(take).ToList();

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new WeekdayCount(d, merged.Count(m => m.MergedAt.UtcDateTime.DayOfWeek == d))).ToList();

        var slowest = merged.OrderByDescending(m => m.CycleTime).ThenBy(m => m.Repo, StringComparer.OrdinalIgnoreCase).Take(3).ToList();

        return new StatsExtras(reviewed, weekdays, slowest, Streak(merged, now), merged.Sum(m => (long)m.Additions), merged.Sum(m => (long)m.Deletions));
    }

    /// <summary>Consecutive weeks with at least one merged PR, counted back from this week (an empty current week does not break a streak that ran up to last week).</summary>
    public static int Streak(IEnumerable<MergedPrRecord> merged, DateTimeOffset now)
    {
        var weeks = merged.Select(m => StatsCalculator.WeekStart(m.MergedAt)).ToHashSet();
        var week = StatsCalculator.WeekStart(now);
        if (!weeks.Contains(week)) week = week.AddDays(-7);
        var count = 0;
        while (weeks.Contains(week))
        {
            count++;
            week = week.AddDays(-7);
        }

        return count;
    }
}

/// <summary>A merged pull request by anyone in the watched repositories.</summary>
public sealed record TeamPr(string Repo, string Author, DateTimeOffset MergedAt, int Lines);

public sealed record PersonCount(string Author, int Merged, long Lines);

public sealed record TeamReport(int Merged, int People, IReadOnlyList<PersonCount> TopPeople, IReadOnlyList<RepoCount> TopRepos);

public static class TeamStats
{
    public static TeamReport Compute(IEnumerable<TeamPr> prs, int take = 6)
    {
        ArgumentNullException.ThrowIfNull(prs);
        var all = prs.ToList();
        var humans = all.Where(p => p.Author.Length > 0 && !p.Author.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)).ToList();

        var people = humans
            .GroupBy(p => p.Author, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PersonCount(g.First().Author, g.Count(), g.Sum(p => (long)p.Lines)))
            .OrderByDescending(p => p.Merged).ThenByDescending(p => p.Lines).ThenBy(p => p.Author, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var repos = all
            .GroupBy(p => p.Repo, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RepoCount(g.First().Repo, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)
            .Take(take).ToList();

        return new TeamReport(all.Count, people.Count, people.Take(take).ToList(), repos);
    }
}
