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

    /// <summary>Base address of Grafana, for example https://grafana.example.com. Empty when not set.</summary>
    public string GrafanaUrl { get; init; } = "";

    /// <summary>Folder that holds the local clones (one sub folder per repository). Empty when not set.</summary>
    public string RepoRoot { get; init; } = "";

    /// <summary>Application name to repository.</summary>
    public IReadOnlyList<RepoBinding> Bindings { get; init; } = Array.Empty<RepoBinding>();

    /// <summary>Namespace prefix to repository.</summary>
    public IReadOnlyList<NamespaceBinding> Namespaces { get; init; } = Array.Empty<NamespaceBinding>();

    /// <summary>Fixes anything invalid or null after loading, so the rest of the app can trust the values.</summary>
    public ErrorsSettings Sanitized() => this with
    {
        GrafanaUrl = RepoBindings.NormalizeUrl(GrafanaUrl) ?? "",
        RepoRoot = RepoRoot?.Trim() ?? "",
        Bindings = RepoBindings.CleanBindings(Bindings),
        Namespaces = RepoBindings.CleanNamespaces(Namespaces),
    };
}
