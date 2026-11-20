using System.Globalization;
using Cshubben.Core.Logic;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>Short text for durations, ages, commit ids and counts.</summary>
public static class PipelineFormat
{
    /// <summary>"45s", "2m 5s", "2m", "1h 12m", "3h". Negative spans are shown as "0s".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var total = (long)span.TotalSeconds;
        if (total < 60) return total.ToString(CultureInfo.InvariantCulture) + "s";
        var minutes = total / 60;
        var seconds = total % 60;
        if (minutes < 60)
            return seconds == 0
                ? minutes.ToString(CultureInfo.InvariantCulture) + "m"
                : minutes.ToString(CultureInfo.InvariantCulture) + "m " + seconds.ToString(CultureInfo.InvariantCulture) + "s";
        var hours = minutes / 60;
        var restMinutes = minutes % 60;
        return restMinutes == 0
            ? hours.ToString(CultureInfo.InvariantCulture) + "h"
            : hours.ToString(CultureInfo.InvariantCulture) + "h " + restMinutes.ToString(CultureInfo.InvariantCulture) + "m";
    }

    /// <summary>"3 h ago", "2 d ago", or "just now".</summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var text = Age.Format(then, now);
        return text == "now" ? "just now" : text + " ago";
    }

    /// <summary>First seven characters of a commit id.</summary>
    public static string ShortSha(string? sha)
    {
        if (string.IsNullOrEmpty(sha)) return string.Empty;
        return sha.Length <= 7 ? sha : sha.Substring(0, 7);
    }

    /// <summary>"1 commit", "3 commits".</summary>
    public static string Count(int n, string singular, string? plural = null) =>
        n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural ?? singular + "s");
}
