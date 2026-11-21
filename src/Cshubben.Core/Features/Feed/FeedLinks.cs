using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Feed;

/// <summary>Where a feed entry leads on github.com. Pure; repository names come from GitHub events, so they are checked first.</summary>
public static partial class FeedLinks
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepoPattern();

    /// <summary>The pull request page when a number is known, else the repository page; null when the repository name is not plain.</summary>
    public static string? UrlFor(string? repo, int? number)
    {
        if (string.IsNullOrEmpty(repo) || !RepoPattern().IsMatch(repo) || repo.Contains("..", StringComparison.Ordinal)) return null;
        var baseUrl = "https://github.com/" + repo;
        return number is > 0 ? baseUrl + "/pull/" + number.Value.ToString(CultureInfo.InvariantCulture) : baseUrl;
    }
}
