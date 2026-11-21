namespace Cshubben.Core.Features.Digest;

/// <summary>How far back an activity summary looks.</summary>
public enum DigestPeriod
{
    Yesterday,
    Week,
    Month,
    Year,
}

/// <summary>A pull request that was merged during the period.</summary>
public sealed record MergedPr(
    string Repo,
    int Number,
    string Title,
    string Author,
    DateTimeOffset MergedAt,
    int Additions,
    int Deletions,
    int ChangedFiles,
    string Url)
{
    public int Size => Additions + Deletions;
}

/// <summary>What was found for one summary.</summary>
/// <param name="Truncated">True when GitHub had more than the app read, so the numbers are a lower bound.</param>
public sealed record DigestInput(
    DigestPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<string> Repos,
    IReadOnlyList<MergedPr> Prs,
    bool Truncated);
