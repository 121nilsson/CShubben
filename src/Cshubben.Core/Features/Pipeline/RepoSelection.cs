namespace Cshubben.Core.Features.Pipeline;

/// <summary>Chooses which repositories the Drift page watches.</summary>
public static class RepoSelection
{
    public const int DefaultMax = 12;

    /// <summary>
    /// Repositories seen in the user's pull requests and feed, narrowed to the followed ones when any are set
    /// (an entry is "owner/name" or a bare "owner"), minus muted ones, plus followed full names that were not seen.
    /// Sorted by name, at most <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<string> Pick(
        IEnumerable<string> seen,
        IEnumerable<string> followed,
        IEnumerable<string> muted,
        int max = DefaultMax)
    {
        var followedList = followed.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        var mutedList = muted.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();

        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in seen)
        {
            if (!LooksLikeRepo(repo)) continue;
            if (followedList.Count > 0 && !Matches(followedList, repo)) continue;
            chosen.TryAdd(repo, repo);
        }

        foreach (var entry in followedList)
            if (LooksLikeRepo(entry)) chosen.TryAdd(entry, entry);

        return chosen.Values
            .Where(r => !Matches(mutedList, r))
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, max))
            .ToList();
    }

    private static bool Matches(List<string> entries, string repo)
    {
        var owner = repo.Substring(0, repo.IndexOf('/'));
        foreach (var entry in entries)
        {
            if (string.Equals(entry, repo, StringComparison.OrdinalIgnoreCase)) return true;
            if (entry.IndexOf('/') < 0 && string.Equals(entry, owner, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool LooksLikeRepo(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var slash = text.IndexOf('/');
        return slash > 0 && slash == text.LastIndexOf('/') && slash < text.Length - 1;
    }
}
