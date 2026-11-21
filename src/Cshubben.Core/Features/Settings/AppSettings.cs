namespace Cshubben.Core.Features.Settings;

/// <summary>Colour theme choice. Unknown or missing values mean <see cref="FollowSystem"/>.</summary>
public static class ThemeChoice
{
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static readonly IReadOnlyList<string> All = new[] { FollowSystem, Light, Dark };

    /// <summary>Lower-cases and validates; anything unknown becomes "system".</summary>
    public static string Normalize(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v == Light || v == Dark ? v : FollowSystem;
    }
}

/// <summary>A model and effort chosen for one Claude function. Empty means "use the default for that function".</summary>
public sealed record ClaudeProfile(string Model = "", string Effort = "");

/// <summary>
/// Everything the user can configure, saved as one small JSON file. Secrets (tokens, API keys) are never part of it.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Repos the user follows (owner/name). Empty means "everything that shows up".</summary>
    public IReadOnlyList<string> FollowedRepos { get; init; } = Array.Empty<string>();

    /// <summary>Repos marked important (owner/name): always watched by Drift and shown first there.</summary>
    public IReadOnlyList<string> ImportantRepos { get; init; } = Array.Empty<string>();

    /// <summary>Repos hidden everywhere (owner/name).</summary>
    public IReadOnlyList<string> MutedRepos { get; init; } = Array.Empty<string>();

    /// <summary>Whole owners or organisations hidden everywhere.</summary>
    public IReadOnlyList<string> MutedOrgs { get; init; } = Array.Empty<string>();

    /// <summary>Show desktop notifications.</summary>
    public bool Notifications { get; init; } = true;

    public bool NotifyReviewRequested { get; init; } = true;

    public bool NotifyCiFailed { get; init; } = true;

    public bool NotifyChangesRequested { get; init; } = true;

    /// <summary>Claude model alias (haiku, sonnet, opus). Empty means the app default.</summary>
    public string ClaudeModel { get; init; } = "";

    /// <summary>Claude effort. Empty means the Claude CLI default.</summary>
    public string ClaudeEffort { get; init; } = "";

    /// <summary>Model and effort per Claude function, keyed by the function name (for example "Activity"). Missing means default.</summary>
    public IReadOnlyDictionary<string, ClaudeProfile> ClaudeProfiles { get; init; } = new Dictionary<string, ClaudeProfile>();

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; init; } = ThemeChoice.FollowSystem;

    /// <summary>One row per PR instead of two lines.</summary>
    public bool CompactLists { get; init; }

    /// <summary>Background refresh interval in seconds while the window is active.</summary>
    public int RefreshSeconds { get; init; } = DefaultRefreshSeconds;

    /// <summary>Notes folder override. Empty means the default folder in the data directory.</summary>
    public string NotesDirectory { get; init; } = "";

    /// <summary>
    /// Opt-in: when false (the default) write actions on GitHub (approve, request changes) are only simulated as a dry run.
    /// </summary>
    public bool AllowGitHubWrites { get; init; }

    public const int DefaultRefreshSeconds = 60;

    public static readonly IReadOnlyList<int> RefreshChoices = new[] { 30, 60, 120, 300 };

    /// <summary>Fixes anything out of range or null after loading, so the rest of the app can trust the values.</summary>
    public AppSettings Sanitized() => this with
    {
        FollowedRepos = RepoList.Clean(FollowedRepos),
        ImportantRepos = RepoList.Clean(ImportantRepos),
        MutedRepos = RepoList.Clean(MutedRepos),
        MutedOrgs = RepoList.Clean(MutedOrgs),
        ClaudeProfiles = (ClaudeProfiles ?? new Dictionary<string, ClaudeProfile>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key.Trim(), kv => new ClaudeProfile(kv.Value.Model?.Trim() ?? "", kv.Value.Effort?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase),
        ClaudeModel = ClaudeModel?.Trim() ?? "",
        ClaudeEffort = ClaudeEffort?.Trim() ?? "",
        Theme = ThemeChoice.Normalize(Theme),
        RefreshSeconds = RefreshSeconds < 15 || RefreshSeconds > 3600 ? DefaultRefreshSeconds : RefreshSeconds,
        NotesDirectory = NotesDirectory?.Trim() ?? "",
    };
}
