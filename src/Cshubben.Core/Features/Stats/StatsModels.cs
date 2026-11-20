namespace Cshubben.Core.Features.Stats;

/// <summary>A pull request of mine that was merged. All times are absolute; durations are derived.</summary>
public sealed record MergedPrRecord(
    string Repo,
    int Number,
    string Title,
    string Author,
    DateTimeOffset OpenedAt,
    DateTimeOffset MergedAt,
    DateTimeOffset? FirstReviewAt,
    int Additions,
    int Deletions,
    int ChangedFiles)
{
    /// <summary>Changed lines (added plus deleted).</summary>
    public int Lines => Additions + Deletions;

    /// <summary>Open to merge. Never negative.</summary>
    public TimeSpan CycleTime => MergedAt > OpenedAt ? MergedAt - OpenedAt : TimeSpan.Zero;

    /// <summary>
    /// Open to first review. Null when nobody reviewed, or when the recorded review time is after the merge (bad data).
    /// Never negative.
    /// </summary>
    public TimeSpan? TimeToFirstReview
    {
        get
        {
            if (FirstReviewAt is not DateTimeOffset review) return null;
            if (review > MergedAt) return null;
            return review > OpenedAt ? review - OpenedAt : TimeSpan.Zero;
        }
    }
}

/// <summary>A review I gave on someone else's pull request.</summary>
public sealed record ReviewRecord(string Repo, int Number, DateTimeOffset At);

public sealed record StatsInput(IReadOnlyList<MergedPrRecord> Merged, IReadOnlyList<ReviewRecord> Reviews);

/// <summary>Distribution of a duration in hours.</summary>
public sealed record Spread(int Count, double Median, double P90, double Mean, double Max)
{
    public static readonly Spread Empty = new(0, 0, 0, 0, 0);
}

/// <summary>A count for one calendar week. <see cref="Start"/> is Monday 00:00 UTC.</summary>
public sealed record WeekBucket(DateTimeOffset Start, int Count);

/// <summary>A median (hours) for one calendar week, with the number of items it is based on.</summary>
public sealed record WeekValue(DateTimeOffset Start, int Count, double MedianHours);

public sealed record SizeBucket(string Label, string Range, int Count, double Fraction);

public sealed record RepoCount(string Repo, int Count);

public sealed record StatsReport(
    int Weeks,
    int MergedCount,
    int ReviewCount,
    Spread CycleTime,
    Spread FirstReview,
    IReadOnlyList<WeekBucket> Throughput,
    IReadOnlyList<WeekBucket> ReviewLoad,
    IReadOnlyList<WeekValue> CycleTrend,
    IReadOnlyList<SizeBucket> Sizes,
    IReadOnlyList<RepoCount> TopRepos);
