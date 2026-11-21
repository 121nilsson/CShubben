using System.Globalization;

namespace Cshubben.Core.Features.Updates;

/// <summary>Pure wording for the About card.</summary>
public static class UpdateText
{
    public static string Status(UpdateCheck? check) => check switch
    {
        null => "",
        { Status: UpdateStatus.Unavailable } => "Updates are only available in an installed copy.",
        { Status: UpdateStatus.UpToDate } => "You are up to date.",
        { Status: UpdateStatus.Available } => $"Version {check.NewVersion} is available.",
        _ => "Could not check for updates." + (string.IsNullOrWhiteSpace(check.Error) ? "" : " " + check.Error),
    };

    public static string LastChecked(DateTimeOffset? checkedAt, DateTimeOffset now)
    {
        if (checkedAt is null) return "Never";
        var age = now - checkedAt.Value;
        if (age < TimeSpan.FromMinutes(1)) return "Just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        return checkedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
