using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Shared;

/// <summary>Where to find the picture of a GitHub user. Pure; the login is untrusted text, so it is checked before it goes into a URL.</summary>
public static partial class AvatarUrl
{
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex LoginPattern();

    /// <summary>
    /// https://github.com/{login}.png at twice the shown size (sharp on scaled screens), or null for anything that is not a plain
    /// user login (bots such as "dependabot[bot]", "ghost", empty or odd text); the UI then shows initials.
    /// </summary>
    public static string? ForGitHub(string? login, double size = 28)
    {
        if (string.IsNullOrEmpty(login) || !LoginPattern().IsMatch(login)) return null;
        if (string.Equals(login, "ghost", StringComparison.OrdinalIgnoreCase)) return null;
        var pixels = (int)Math.Clamp(Math.Round(size * 2), 32, 256);
        return "https://github.com/" + login + ".png?size=" + pixels.ToString(CultureInfo.InvariantCulture);
    }
}
