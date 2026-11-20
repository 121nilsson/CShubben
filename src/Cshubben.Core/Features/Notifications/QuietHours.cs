using System.Globalization;

namespace Cshubben.Core.Features.Notifications;

/// <summary>Pure helpers for the "do not disturb" window. Times are minutes after local midnight.</summary>
public static class QuietHours
{
    /// <summary>
    /// True when <paramref name="minuteOfDay"/> lies in [start, end). A window that ends before it starts
    /// wraps over midnight (22:00 to 07:00). Equal start and end mean "no window".
    /// </summary>
    public static bool IsQuiet(int startMinute, int endMinute, int minuteOfDay)
    {
        if (startMinute == endMinute) return false;
        if (startMinute < endMinute) return minuteOfDay >= startMinute && minuteOfDay < endMinute;
        return minuteOfDay >= startMinute || minuteOfDay < endMinute;
    }

    /// <summary>Minutes after midnight of the wall-clock time carried by the value (pass local time).</summary>
    public static int MinuteOfDay(DateTimeOffset time) => time.Hour * 60 + time.Minute;

    /// <summary>"07:30".</summary>
    public static string Format(int minuteOfDay)
    {
        var m = ((minuteOfDay % 1440) + 1440) % 1440;
        return (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses "HH:mm"; null when invalid.</summary>
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return null;
        if (h > 23 || m > 59) return null;
        return h * 60 + m;
    }
}
