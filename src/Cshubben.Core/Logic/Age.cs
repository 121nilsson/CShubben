namespace Cshubben.Core.Logic;

/// <summary>Short relative time such as "now", "5 min", "3 h", "2 d".</summary>
public static class Age
{
    public static string Format(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h";
        if (span < TimeSpan.FromDays(60)) return $"{(int)span.TotalDays} d";
        return $"{(int)(span.TotalDays / 30)} mo";
    }
}
