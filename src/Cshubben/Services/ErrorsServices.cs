using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// Where the Errors page gets its data. This is the one place that joins the Errors feature (logs) with the Settings feature
/// (addresses, application bindings) and the credential store; the features do not know about each other.
/// </summary>
static class ErrorsServices
{
    private static readonly object Gate = new();
    private static string? _sessionUrl;
    private static McpOAuthSession? _session;
    private static McpClient? _client;

    /// <summary>The sign-in for the MCP server in the settings (one per address, kept so the access token is reused).</summary>
    public static McpOAuthSession SessionFor(AppServices services, string mcpUrl)
    {
        lock (Gate)
        {
            if (_session is null || !string.Equals(_sessionUrl, mcpUrl, StringComparison.Ordinal))
            {
                _session = new McpOAuthSession(services.Http, mcpUrl, services.Secrets);
                _client = new McpClient(services.Http, mcpUrl, ct => _session.GetAccessTokenAsync(ct));
                _sessionUrl = mcpUrl;
            }

            return _session;
        }
    }

    private static McpClient ClientFor(AppServices services, string mcpUrl)
    {
        SessionFor(services, mcpUrl);
        lock (Gate) return _client!;
    }

    /// <summary>True when the settings name everything the chosen source needs (not whether the sign-in or token is valid).</summary>
    public static bool IsConfigured(ErrorsSettings settings) =>
        settings.Source == ErrorsSettings.SourceGrafana
            ? settings.GrafanaUrl.Length > 0
            : settings.McpUrl.Length > 0 && settings.Workspace.Length > 0 && settings.ResourceGroup.Length > 0;

    /// <summary>The source for the current settings and a cache key that changes whenever the answer would; null when not set up.</summary>
    public static (IErrorSource Source, string Key)? SourceFor(AppServices services, ErrorsSettings settings)
    {
        var apps = settings.Bindings.Select(b => b.App).ToList();

        if (services.ForcedDemo)
            return (new DemoErrorSource(), "errors|demo|" + string.Join("|", apps));

        if (settings.Source == ErrorsSettings.SourceGrafana)
        {
            var token = services.Secrets.Get(SecretKeys.GrafanaToken);
            if (settings.GrafanaUrl.Length == 0 || string.IsNullOrWhiteSpace(token)) return null;

            var client = new GrafanaClient(
                services.Http,
                () => services.Settings.Errors.GrafanaUrl,
                () => services.Secrets.Get(SecretKeys.GrafanaToken));
            var query = new ErrorsQuery(settings.Namespace, settings.IncludeWarnings, settings.LokiDatasource, apps);
            var key = string.Join(
                "|",
                "errors",
                "grafana",
                settings.GrafanaUrl,
                settings.LokiDatasource,
                settings.Namespace,
                settings.IncludeWarnings ? "w" : "e",
                StableKey.Hash(token),
                string.Join(",", apps));
            return (new GrafanaErrorSource(client, query), key);
        }

        if (!IsConfigured(settings)) return null;
        var azure = new AzureLogsQuery(settings.Workspace, settings.ResourceGroup, settings.IncludeWarnings, apps);
        var azureKey = string.Join(
            "|",
            "errors",
            "azure",
            settings.McpUrl,
            settings.Workspace,
            settings.ResourceGroup,
            settings.IncludeWarnings ? "w" : "e",
            string.Join(",", apps));
        return (new AzureLogsErrorSource(ClientFor(services, settings.McpUrl), azure), azureKey);
    }

    /// <summary>The cache key of the groups of one application.</summary>
    public static string GroupsKey(string sourceKey, string app) => sourceKey + "|groups|" + app;

    /// <summary>The cache key of last week's counts for the hour that ends the given window, so they always line up with the counts they are compared with.</summary>
    public static string LastWeekKey(string sourceKey, ErrorWindow current) =>
        sourceKey + "|lastweek|" + current.End.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The same hour one week ago for every application. Null, never an error, when it cannot be read (for example when the logs
    /// do not reach back that far): the page is complete without the comparison.
    /// </summary>
    public static async Task<LastWeekLogs?> LastWeekAsync(IErrorSource source, string sourceKey, ErrorWindow current, CancellationToken cancellationToken)
    {
        try
        {
            return await PageCaches.ErrorLastWeek.GetAsync(LastWeekKey(sourceKey, current), PageCaches.ErrorsTtl, () => source.LoadLastWeekAsync(current, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The cache key of the deployments of one repository and environment.</summary>
    public static string DeploysKey(string sourceKey, string repo, string environment) => sourceKey + "|deploys|" + repo + "|" + environment;

    /// <summary>
    /// The recent deployments of the repository an application is bound to (newest last), for the chart markers and for tying a
    /// spike to a deployment. Empty, never an error, when the application is not bound, GitHub is not connected or the read fails:
    /// the page is complete without them.
    /// </summary>
    public static async Task<IReadOnlyList<DeployedVersion>> DeploysAsync(
        AppServices services, ErrorsSettings settings, string sourceKey, string app, bool demo, CancellationToken cancellationToken)
    {
        if (demo) return DemoErrorSource.DeploysFor(app, DateTimeOffset.UtcNow);

        var repo = RepoBindings.RepoFor(settings, app);
        if (repo is null || services.GitHub is not { } hub) return Array.Empty<DeployedVersion>();

        var environment = ErrorEnvironment.Guess(settings.Workspace, settings.ResourceGroup, settings.Namespace);
        return await PageCaches.ErrorDeploys.GetAsync(
            DeploysKey(sourceKey, repo, environment),
            PageCaches.ErrorsTtl,
            async () =>
            {
                try
                {
                    return await new ErrorContextLoader(hub.Rest).LoadDeploysAsync(repo, environment, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is GitHubException or HttpRequestException or ArgumentException)
                {
                    return Array.Empty<DeployedVersion>();
                }
            }).ConfigureAwait(false);
    }
}
