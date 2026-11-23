using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// Where the Errors page gets its data. This is the one place that joins the Errors feature (logs) with the Settings feature
/// (address, token, application bindings); the two features do not know about each other.
/// </summary>
static class ErrorsServices
{
    /// <summary>The source for the current settings and a cache key that changes whenever the answer would; null when not connected.</summary>
    public static (IErrorSource Source, string Key)? SourceFor(AppServices services, ErrorsSettings settings)
    {
        var apps = settings.Bindings.Select(b => b.App).ToList();
        var query = new ErrorsQuery(settings.Namespace, settings.IncludeWarnings, settings.LokiDatasource, apps);

        if (services.ForcedDemo)
            return (new DemoErrorSource(), "errors|demo|" + string.Join("|", apps));

        var token = services.Secrets.Get(SecretKeys.GrafanaToken);
        if (settings.GrafanaUrl.Length == 0 || string.IsNullOrWhiteSpace(token)) return null;

        var client = new GrafanaClient(
            services.Http,
            () => services.Settings.Errors.GrafanaUrl,
            () => services.Secrets.Get(SecretKeys.GrafanaToken));
        var key = string.Join(
            "|",
            "errors",
            settings.GrafanaUrl,
            settings.LokiDatasource,
            settings.Namespace,
            settings.IncludeWarnings ? "w" : "e",
            StableKey.Hash(token),
            string.Join(",", apps));
        return (new GrafanaErrorSource(client, query), key);
    }

    /// <summary>The cache key of the groups of one application.</summary>
    public static string GroupsKey(string sourceKey, string app) => sourceKey + "|groups|" + app;
}
