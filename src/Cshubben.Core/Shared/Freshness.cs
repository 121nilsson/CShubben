using Cshubben.Core.Logic;

namespace Cshubben.Core.Shared;

/// <summary>Text for "how fresh is the data on screen".</summary>
public static class Freshness
{
    /// <summary>"Updated just now", "Updated 2 min ago", "Updated 3 h ago". A negative age counts as just now.</summary>
    public static string Label(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        if (now - updatedAt < TimeSpan.FromMinutes(1)) return "Updated just now";
        return "Updated " + Age.Format(updatedAt, now) + " ago";
    }
}
