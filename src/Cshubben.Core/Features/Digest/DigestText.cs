using System.Globalization;
using System.Text;

namespace Cshubben.Core.Features.Digest;

/// <summary>
/// Turns merged pull requests into the compact text a summary is written from: totals, per repo, per author, per day or month,
/// and a list of the pull requests (all of them for a short period, the biggest ones for a long one). Pure and bounded in size.
/// The text contains pull request titles, which are untrusted; the prompt builder puts it inside the data section.
/// </summary>
public static class DigestText
{
    public const int MaxListed = 120;
    public const int MaxListedYear = 80;

    public static IReadOnlyList<MergedPr> Filter(IEnumerable<MergedPr> prs, string? onlyAuthor) =>
        string.IsNullOrWhiteSpace(onlyAuthor)
            ? prs.ToList()
            : prs.Where(p => string.Equals(p.Author, onlyAuthor, StringComparison.OrdinalIgnoreCase)).ToList();

    public static string Build(DigestInput input, string? onlyAuthor = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prs = Filter(input.Prs, onlyAuthor);
        var sb = new StringBuilder();
        sb.Append("Repositories covered (").Append(input.Repos.Count).Append("): ").Append(string.Join(", ", input.Repos.Take(40)));
        if (input.Repos.Count > 40) sb.Append(" and ").Append(input.Repos.Count - 40).Append(" more");
        sb.Append('\n');
        if (!string.IsNullOrWhiteSpace(onlyAuthor)) sb.Append("Only pull requests by ").Append(One(onlyAuthor, 60)).Append('\n');
        if (input.Truncated) sb.Append("Note: GitHub had more pull requests than were read; the numbers below are a lower bound.\n");

        if (prs.Count == 0)
        {
            sb.Append("No pull requests were merged in this period.\n");
            return sb.ToString();
        }

        var additions = prs.Sum(p => (long)p.Additions);
        var deletions = prs.Sum(p => (long)p.Deletions);
        sb.Append("Totals: ").Append(prs.Count).Append(" merged pull requests by ")
            .Append(prs.Select(p => p.Author).Distinct(StringComparer.OrdinalIgnoreCase).Count()).Append(" people in ")
            .Append(prs.Select(p => p.Repo).Distinct(StringComparer.OrdinalIgnoreCase).Count()).Append(" repositories, +")
            .Append(additions).Append("/-").Append(deletions).Append(" lines\n\n");

        sb.Append("Per repository (merged pull requests, lines changed):\n");
        foreach (var g in prs.GroupBy(p => p.Repo, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Take(40))
            sb.Append("- ").Append(One(g.Key, 80)).Append(": ").Append(g.Count()).Append(", ").Append(g.Sum(p => (long)p.Size)).Append('\n');

        sb.Append("\nPer author:\n");
        foreach (var g in prs.GroupBy(p => p.Author, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Take(25))
            sb.Append("- ").Append(One(g.Key.Length == 0 ? "(unknown)" : g.Key, 40)).Append(": ").Append(g.Count()).Append('\n');

        var byMonth = input.Period == DigestPeriod.Year;
        sb.Append(byMonth ? "\nPer month:\n" : "\nPer day:\n");
        var buckets = prs.GroupBy(p => byMonth ? p.MergedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture) : p.MergedAt.ToString("yyyy-MM-dd ddd", CultureInfo.InvariantCulture))
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var g in buckets) sb.Append("- ").Append(g.Key).Append(": ").Append(g.Count()).Append('\n');

        var limit = input.Period == DigestPeriod.Year ? MaxListedYear : MaxListed;
        var listed = prs.Count <= limit ? prs : prs.OrderByDescending(p => p.Size).Take(limit).ToList();
        sb.Append(prs.Count <= limit ? "\nAll merged pull requests:\n" : "\nThe " + limit + " biggest merged pull requests (by lines changed):\n");
        foreach (var p in listed.OrderBy(p => p.Repo, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.MergedAt))
        {
            sb.Append("- ").Append(One(p.Repo, 80)).Append('#').Append(p.Number).Append(" \"").Append(One(p.Title, 90)).Append("\" by ")
                .Append(One(p.Author, 40)).Append(" | +").Append(p.Additions).Append("/-").Append(p.Deletions).Append(" | ")
                .Append(p.MergedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (p.Url.StartsWith("https://github.com/", StringComparison.Ordinal)) sb.Append(" | ").Append(One(p.Url, 160));
            sb.Append('\n');
        }

        if (prs.Count > limit) sb.Append("(").Append(prs.Count - limit).Append(" smaller pull requests are counted above but not listed)\n");
        return sb.ToString();
    }

    private static string One(string? text, int max)
    {
        var s = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
