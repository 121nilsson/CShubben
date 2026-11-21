using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Settings;

namespace Cshubben.Core.Composition;

/// <summary>Works out which model and effort a Claude function uses. The one place that knows both the Claude and the Settings feature.</summary>
public static class ClaudeSettings
{
    /// <summary>
    /// Order: what the user chose for this function, then the function's own default (cheap for summaries), then the general
    /// Claude setting. Null means "let Claude Code decide".
    /// </summary>
    public static (string? Model, string? Effort) Resolve(AppSettings settings, ClaudeFeature feature)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ClaudeProfiles.TryGetValue(feature.ToString(), out var chosen);

        var model = First(chosen?.Model, ClaudeFeatures.DefaultModel(feature), settings.ClaudeModel);
        var effort = First(chosen?.Effort, ClaudeFeatures.DefaultEffort(feature), settings.ClaudeEffort);
        return (model, effort);
    }

    /// <summary>Saves a choice for one function; empty values remove it so the default applies again.</summary>
    public static AppSettings With(AppSettings settings, ClaudeFeature feature, string model, string effort)
    {
        var profiles = new Dictionary<string, ClaudeProfile>(settings.ClaudeProfiles, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort)) profiles.Remove(feature.ToString());
        else profiles[feature.ToString()] = new ClaudeProfile(model.Trim(), effort.Trim());
        return settings with { ClaudeProfiles = profiles };
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
