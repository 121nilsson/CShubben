namespace Cshubben.Core.Features.Settings;

/// <summary>
/// Binds a log application name (the Grafana "appname" label, for example "binders-api") to its GitHub repository (owner/name).
/// </summary>
public sealed record RepoBinding(string App = "", string Repo = "");

/// <summary>
/// Binds a .NET namespace prefix (for example "iBinder.BuildingBlocks") to a repository. Stack frames in the logs carry
/// namespaces, so this finds the repo of a shared library even when the failing app is a different one.
/// </summary>
public sealed record NamespaceBinding(string Prefix = "", string Repo = "");

/// <summary>
/// Settings of the Errors module (error spikes from the logs, tied to deploys). The module is off by default: when it is
/// off there is no page, no background work and no token is needed. The Grafana token is never part of this record.
/// </summary>
public sealed record ErrorsSettings
{
    /// <summary>Shows the Errors page and allows the module to talk to Grafana.</summary>
    public bool Enabled { get; init; }

    /// <summary>Where the logs are read from: <see cref="SourceAzure"/> (Azure Monitor through the MCP server) or <see cref="SourceGrafana"/>.</summary>
    public string Source { get; init; } = SourceAzure;

    /// <summary>Address of the MCP server in front of Azure Monitor. Not a secret, but internal, so it is a setting and not code.</summary>
    public string McpUrl { get; init; } = "";

    /// <summary>Name of the Log Analytics workspace.</summary>
    public string Workspace { get; init; } = "";

    /// <summary>Resource group of the workspace.</summary>
    public string ResourceGroup { get; init; } = "";

    /// <summary>Base address of Grafana, for example https://grafana.example.com. Empty when not set.</summary>
    public string GrafanaUrl { get; init; } = "";

    /// <summary>Uid of the Loki data source in Grafana. Empty means "use the first Loki data source".</summary>
    public string LokiDatasource { get; init; } = "";

    /// <summary>Value of the "namespace" label the applications run in.</summary>
    public string Namespace { get; init; } = DefaultNamespace;

    /// <summary>Also read warnings, not only errors.</summary>
    public bool IncludeWarnings { get; init; }

    /// <summary>Folder that holds the local clones (one sub folder per repository). Empty when not set.</summary>
    public string RepoRoot { get; init; } = "";

    /// <summary>Application name to repository.</summary>
    public IReadOnlyList<RepoBinding> Bindings { get; init; } = Array.Empty<RepoBinding>();

    /// <summary>Namespace prefix to repository.</summary>
    public IReadOnlyList<NamespaceBinding> Namespaces { get; init; } = Array.Empty<NamespaceBinding>();

    public const string DefaultNamespace = "default";
    public const string SourceAzure = "azure";
    public const string SourceGrafana = "grafana";

    /// <summary>Fixes anything invalid or null after loading, so the rest of the app can trust the values.</summary>
    public ErrorsSettings Sanitized() => this with
    {
        Source = string.Equals(Source?.Trim(), SourceGrafana, StringComparison.OrdinalIgnoreCase) ? SourceGrafana : SourceAzure,
        McpUrl = RepoBindings.NormalizeUrl(McpUrl) ?? "",
        Workspace = RepoBindings.NormalizeApp(Workspace) ?? "",
        ResourceGroup = RepoBindings.NormalizeApp(ResourceGroup) ?? "",
        GrafanaUrl = RepoBindings.NormalizeUrl(GrafanaUrl) ?? "",
        LokiDatasource = LokiDatasource?.Trim() ?? "",
        Namespace = RepoBindings.NormalizeApp(Namespace) ?? DefaultNamespace,
        RepoRoot = RepoRoot?.Trim() ?? "",
        Bindings = RepoBindings.CleanBindings(Bindings),
        Namespaces = RepoBindings.CleanNamespaces(Namespaces),
    };
}
