using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Settings;

/// <summary>Pure helpers for the repository lists in settings (followed, muted). Names compare case-insensitively.</summary>
public static class RepoList
{
    private static readonly Regex RepoPattern = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

    private static readonly Regex OwnerPattern = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant);

    /// <summary>True for "owner/name".</summary>
    public static bool IsRepo(string? text) => text is not null && RepoPattern.IsMatch(text.Trim());

    /// <summary>True for "owner/name" or a bare owner.</summary>
    public static bool IsRepoOrOwner(string? text) =>
        text is not null && (RepoPattern.IsMatch(text.Trim()) || OwnerPattern.IsMatch(text.Trim()));

    /// <summary>
    /// Accepts "owner/name", a github.com URL or a bare owner, and returns the normalised name,
    /// or null when the text is not a valid name.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        const string https = "https://github.com/";
        const string http = "http://github.com/";
        if (t.StartsWith(https, StringComparison.OrdinalIgnoreCase)) t = t.Substring(https.Length);
        else if (t.StartsWith(http, StringComparison.OrdinalIgnoreCase)) t = t.Substring(http.Length);
        t = t.Trim('/');
        if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
        var parts = t.Split('/');
        if (parts.Length > 2) t = parts[0] + "/" + parts[1];
        return IsRepoOrOwner(t) ? t : null;
    }

    /// <summary>Adds a name when valid and not already present. Returns the list unchanged otherwise.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> list, string? text)
    {
        var name = Normalize(text);
        if (name is null) return list;
        if (list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) return list;
        return list.Concat(new[] { name }).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> list, string name) =>
        list.Where(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Drops nulls, blanks, invalid names and duplicates; sorts.</summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string?>? items)
    {
        if (items is null) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var item in items)
        {
            var name = Normalize(item);
            if (name is null) continue;
            if (result.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(name);
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when the repo (owner/name) is muted, directly or through its owner.</summary>
    public static bool IsMuted(AppSettings settings, string repo)
    {
        if (settings.MutedRepos.Any(x => string.Equals(x, repo, StringComparison.OrdinalIgnoreCase))) return true;
        var slash = repo.IndexOf('/');
        var owner = slash > 0 ? repo.Substring(0, slash) : repo;
        return settings.MutedOrgs.Any(x => string.Equals(x, owner, StringComparison.OrdinalIgnoreCase));
    }
}
