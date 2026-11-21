using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cshubben.Core.Features.Diagnostics;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// Builds the read-only smoke test for this machine: GitHub, Linear, Claude CLI, credential store, settings file and data folders.
/// The generic runner and the checks that need only Core interfaces live in Core (Features/Diagnostics); this class adds the
/// checks that need several features, because the composition root is where features meet. Nothing here writes to GitHub or Linear.
/// </summary>
static class AppDiagnostics
{
    public static async Task<DiagnosticReport> RunAsync(AppServices services, CancellationToken cancellationToken = default)
    {
        var report = await DiagnosticsRunner.RunAsync(
            Build(services), DateTimeOffset.UtcNow, AppInfo(services), null, KnownSecrets(services), ErrorText.FactsFor, cancellationToken);
        return report;
    }

    /// <summary>The report as plain text with every known secret removed.</summary>
    public static string Text(AppServices services, DiagnosticReport report) => report.ToText(KnownSecrets(services));

    public static IReadOnlyList<DiagnosticCheck> Build(AppServices services)
    {
        var checks = new List<DiagnosticCheck>();
        if (services.ForcedDemo)
        {
            checks.Add(BuiltInChecks.Skipped("GitHub", "Demo mode (--demo): GitHub is not used."));
            checks.Add(BuiltInChecks.Skipped("Linear", "Demo mode (--demo): Linear is not used."));
        }
        else
        {
            checks.Add(new DiagnosticCheck("GitHub", ct => CheckGitHubAsync(services, ct)));
            checks.Add(new DiagnosticCheck("Linear", ct => CheckLinearAsync(services, ct)));
        }

        checks.Add(new DiagnosticCheck("Claude CLI", ct => CheckClaudeAsync(services, ct)));
        checks.Add(BuiltInChecks.SecretStore("Credential store", services.Secrets));
        checks.Add(new DiagnosticCheck("Settings file", _ => Task.FromResult(CheckSettings(services))));
        if (services.ForcedDemo)
        {
            checks.Add(BuiltInChecks.Skipped("Data folder", "Demo mode (--demo): nothing is saved."));
        }
        else
        {
            checks.Add(BuiltInChecks.DataDirectoryWritable("Data folder", services.DataFolder));
            checks.Add(BuiltInChecks.DataDirectoryWritable("Notes folder", services.NotesFolder));
        }

        return checks;
    }

    /// <summary>Token values we know about, so the report can be scrubbed of them.</summary>
    public static IReadOnlyList<string?> KnownSecrets(AppServices services) => new[]
    {
        services.Secrets.Get(SecretKeys.LinearApiKey),
        services.Secrets.Get(SecretKeys.ClaudeOAuthToken),
        Environment.GetEnvironmentVariable("LINEAR_API_KEY"),
        Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
        Environment.GetEnvironmentVariable("GH_TOKEN"),
        Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN"),
    };

    private static string AppInfo(AppServices services)
    {
        var version = typeof(AppDiagnostics).Assembly.GetName().Version?.ToString() ?? "unknown";
        return "App " + version + ", " + RuntimeInformation.FrameworkDescription + ", " + RuntimeInformation.OSDescription
            + "\nDemo: " + (services.ForcedDemo ? "yes" : "no")
            + ", writes to GitHub: " + (services.Settings.AllowGitHubWrites ? "allowed" : "off (dry run)");
    }

    private static async Task<DiagnosticResult> CheckGitHubAsync(AppServices services, CancellationToken ct)
    {
        const string name = "GitHub";
        var token = await services.ProbeGitHubTokenAsync(ct);
        if (!token.Found)
            return DiagnosticResult.Fail(name, "No token found. Set GITHUB_TOKEN or GH_TOKEN, or run `gh auth login`, then run the checks again.");

        await services.GetHubDataAsync(ct);
        var hub = services.GitHub;
        if (hub is null)
            return DiagnosticResult.Fail(name, services.DemoReason ?? "Could not set up the GitHub connection.");

        // Read-only: viewer lookup, search for my PRs and review requests, enrichment, feed.
        var loaded = await BuiltInChecks.HubData(name, hub).Run(ct);
        var detail = "token from " + token.Source + "; " + loaded.Detail;
        if (hub.LastWarnings.Count > 0)
        {
            detail += "\nPartial data: " + string.Join(" ", hub.LastWarnings.Take(3));
            if (loaded.Status == DiagnosticStatus.Ok) loaded = DiagnosticResult.Warn(name, detail);
        }

        if (hub.Rest.LastRateLimit is { Remaining: { } left } rate)
        {
            detail += "\nRate limit: " + left.ToString("N0", CultureInfo.InvariantCulture)
                + (rate.Limit is { } limit ? " of " + limit.ToString("N0", CultureInfo.InvariantCulture) : "") + " left";
        }

        return loaded with { Detail = detail };
    }

    private static async Task<DiagnosticResult> CheckLinearAsync(AppServices services, CancellationToken ct)
    {
        const string name = "Linear";
        var key = LinearClient.ResolveApiKey(services.Secrets);
        if (key is null)
            return DiagnosticResult.Warn(name, "No API key saved. Linear is optional; add a key in Settings to use it.");

        // Read-only: the "viewer" query.
        var viewer = await new LinearClient(services.Http, key).GetViewerAsync(ct);
        return DiagnosticResult.Ok(name, "connected as " + viewer.Name);
    }

    private static async Task<DiagnosticResult> CheckClaudeAsync(AppServices services, CancellationToken ct)
    {
        const string name = "Claude CLI";
        var info = await services.CheckClaudeAsync(ct);
        if (info.Path is null)
            return DiagnosticResult.Warn(name, "claude not found. Install Claude Code, or set CSHUBBEN_CLAUDE_PATH to the executable. Summaries and drafts need it; the rest of the app works without it.");
        if (info.Version is null)
            return DiagnosticResult.Warn(name, "found at " + info.Path + " but `claude --version` did not answer. " + info.Auth);
        return DiagnosticResult.Ok(name, info.Path + ", version " + info.Version + ". " + info.Auth);
    }

    private static DiagnosticResult CheckSettings(AppServices services)
    {
        const string name = "Settings file";
        var s = services.Settings;
        var summary = "followed " + s.FollowedRepos.Count.ToString(CultureInfo.InvariantCulture)
            + ", muted repos " + s.MutedRepos.Count.ToString(CultureInfo.InvariantCulture)
            + ", muted orgs " + s.MutedOrgs.Count.ToString(CultureInfo.InvariantCulture);
        if (services.SettingsStore is not FileSettingsStore file)
            return DiagnosticResult.Ok(name, "in memory (demo); " + summary);
        if (!File.Exists(file.FilePath))
            return DiagnosticResult.Ok(name, "no file yet, defaults in use (" + file.FilePath + "); " + summary);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file.FilePath));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? DiagnosticResult.Ok(name, file.FilePath + "; " + summary)
                : DiagnosticResult.Warn(name, "settings.json is not a JSON object; defaults are in use. Delete it to start fresh.");
        }
        catch (JsonException)
        {
            return DiagnosticResult.Warn(name, "settings.json is damaged; defaults are in use and the next save replaces it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiagnosticResult.Fail(name, "Cannot read " + file.FilePath + ": " + ex.Message);
        }
    }
}
