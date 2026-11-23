using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Settings;

/// <summary>What an import of shared mappings did. <see cref="Error"/> is set when nothing was changed.</summary>
public sealed record BindingImportResult(ErrorsSettings Settings, int Added, int Updated, int Skipped, string? Error)
{
    public bool Ok => Error is null;

    /// <summary>One calm sentence for the UI.</summary>
    public string Message
    {
        get
        {
            if (Error is not null) return Error;
            var text = "Imported " + Added + " new and " + Updated + " changed mappings";
            return Skipped > 0 ? text + ", skipped " + Skipped + " invalid." : text + ".";
        }
    }
}

/// <summary>
/// Pure helpers for the application-to-repository mappings of the Errors module: validation, lookup, suggestions and a
/// small JSON format to share the mappings inside the team. Names compare case-insensitively.
/// </summary>
public static class RepoBindings
{
    private static readonly Regex AppPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    private static readonly Regex PrefixPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ShareOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // ---- validation ----

    /// <summary>The application name (the "appname" label), or null when it is not a plain name like "binders-api".</summary>
    public static string? NormalizeApp(string? text)
    {
        var t = text?.Trim();
        return t is not null && AppPattern.IsMatch(t) ? t : null;
    }

    /// <summary>"owner/name" (also from a github.com URL), or null. A bare owner is not a repository.</summary>
    public static string? NormalizeRepo(string? text)
    {
        var name = RepoList.Normalize(text);
        return name is not null && RepoList.IsRepo(name) ? name : null;
    }

    /// <summary>A namespace prefix like "iBinder.BuildingBlocks". A trailing ".*" or "." is accepted and dropped.</summary>
    public static string? NormalizePrefix(string? text)
    {
        var t = text?.Trim().TrimEnd('*').TrimEnd('.');
        return !string.IsNullOrEmpty(t) && PrefixPattern.IsMatch(t) ? t : null;
    }

    /// <summary>
    /// A full http(s) address without a trailing slash, or null. Plain http is only accepted for localhost, because the
    /// Grafana token travels with every request.
    /// </summary>
    public static string? NormalizeUrl(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return null;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    // ---- lists ----

    /// <summary>Drops invalid entries and duplicates (first one wins) and sorts by application name.</summary>
    public static IReadOnlyList<RepoBinding> CleanBindings(IEnumerable<RepoBinding?>? items)
    {
        if (items is null) return Array.Empty<RepoBinding>();
        var result = new List<RepoBinding>();
        foreach (var item in items)
        {
            if (item is null) continue;
            var app = NormalizeApp(item.App);
            var repo = NormalizeRepo(item.Repo);
            if (app is null || repo is null) continue;
            if (result.Any(x => Same(x.App, app))) continue;
            result.Add(new RepoBinding(app, repo));
        }

        return result.OrderBy(x => x.App, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Drops invalid entries and duplicates (first one wins) and sorts by prefix.</summary>
    public static IReadOnlyList<NamespaceBinding> CleanNamespaces(IEnumerable<NamespaceBinding?>? items)
    {
        if (items is null) return Array.Empty<NamespaceBinding>();
        var result = new List<NamespaceBinding>();
        foreach (var item in items)
        {
            if (item is null) continue;
            var prefix = NormalizePrefix(item.Prefix);
            var repo = NormalizeRepo(item.Repo);
            if (prefix is null || repo is null) continue;
            if (result.Any(x => Same(x.Prefix, prefix))) continue;
            result.Add(new NamespaceBinding(prefix, repo));
        }

        return result.OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds or replaces the binding for an application. Invalid input returns the list unchanged.</summary>
    public static IReadOnlyList<RepoBinding> Set(IReadOnlyList<RepoBinding> list, string? app, string? repo)
    {
        var a = NormalizeApp(app);
        var r = NormalizeRepo(repo);
        if (a is null || r is null) return list;
        return CleanBindings(list.Where(x => !Same(x.App, a)).Append(new RepoBinding(a, r)));
    }

    public static IReadOnlyList<RepoBinding> Remove(IReadOnlyList<RepoBinding> list, string app) =>
        list.Where(x => !Same(x.App, app)).ToList();

    /// <summary>Adds or replaces the binding for a namespace prefix. Invalid input returns the list unchanged.</summary>
    public static IReadOnlyList<NamespaceBinding> SetNamespace(IReadOnlyList<NamespaceBinding> list, string? prefix, string? repo)
    {
        var p = NormalizePrefix(prefix);
        var r = NormalizeRepo(repo);
        if (p is null || r is null) return list;
        return CleanNamespaces(list.Where(x => !Same(x.Prefix, p)).Append(new NamespaceBinding(p, r)));
    }

    public static IReadOnlyList<NamespaceBinding> RemoveNamespace(IReadOnlyList<NamespaceBinding> list, string prefix) =>
        list.Where(x => !Same(x.Prefix, prefix)).ToList();

    // ---- lookup ----

    /// <summary>The repository bound to an application name, or null.</summary>
    public static string? RepoFor(ErrorsSettings settings, string? app)
    {
        var name = app?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var binding in settings.Bindings)
            if (Same(binding.App, name)) return binding.Repo;
        return null;
    }

    /// <summary>
    /// The repository for a type or namespace name from a stack frame. The longest matching prefix wins, and a prefix only
    /// matches whole name parts ("iBinder.Core" matches "iBinder.Core.Foo" but not "iBinder.CoreX").
    /// </summary>
    public static string? RepoForNamespace(ErrorsSettings settings, string? typeName)
    {
        var name = typeName?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        string? best = null;
        var bestLength = -1;
        foreach (var binding in settings.Namespaces)
        {
            if (binding.Prefix.Length <= bestLength) continue;
            var matches = name.Equals(binding.Prefix, StringComparison.OrdinalIgnoreCase)
                          || name.StartsWith(binding.Prefix + ".", StringComparison.OrdinalIgnoreCase);
            if (!matches) continue;
            best = binding.Repo;
            bestLength = binding.Prefix.Length;
        }

        return best;
    }

    /// <summary>
    /// A repository whose name equals the application name, when exactly one known repository does. Used to prefill the
    /// "add mapping" form, so "binders-api" suggests "acme/binders-api".
    /// </summary>
    public static string? Suggest(string? app, IEnumerable<string> knownRepos)
    {
        var name = NormalizeApp(app);
        if (name is null) return null;
        var matches = knownRepos
            .Select(NormalizeRepo)
            .Where(r => r is not null && Same(r.Substring(r.IndexOf('/') + 1), name))
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // ---- sharing inside the team ----

    /// <summary>The mappings as one line of JSON. Grafana address, token and local folders are not part of it.</summary>
    public static string ToJson(ErrorsSettings settings) =>
        JsonSerializer.Serialize(
            new ShareFile(
                1,
                settings.Bindings.Select(b => new ShareBinding(b.App, b.Repo)).ToList(),
                settings.Namespaces.Select(n => new ShareNamespace(n.Prefix, n.Repo)).ToList()),
            ShareOptions);

    /// <summary>
    /// Merges shared mappings into the current settings. An application or prefix that already exists gets the imported
    /// repository. Broken JSON changes nothing and says why.
    /// </summary>
    public static BindingImportResult Import(ErrorsSettings current, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new BindingImportResult(current, 0, 0, 0, "Nothing to import. Paste the copied mappings first.");

        ShareFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ShareFile>(json, ShareOptions);
        }
        catch (JsonException)
        {
            return new BindingImportResult(current, 0, 0, 0, "That is not valid mappings JSON.");
        }

        if (file is null)
            return new BindingImportResult(current, 0, 0, 0, "That is not valid mappings JSON.");

        var bindings = current.Bindings;
        var namespaces = current.Namespaces;
        int added = 0, updated = 0, skipped = 0;

        foreach (var item in file.Bindings ?? new List<ShareBinding>())
        {
            var app = item is null ? null : NormalizeApp(item.App);
            var repo = item is null ? null : NormalizeRepo(item.Repo);
            if (app is null || repo is null)
            {
                skipped++;
                continue;
            }

            var existing = bindings.FirstOrDefault(x => Same(x.App, app));
            if (existing is null) added++;
            else if (!Same(existing.Repo, repo)) updated++;
            else continue;
            bindings = Set(bindings, app, repo);
        }

        foreach (var item in file.Namespaces ?? new List<ShareNamespace>())
        {
            var prefix = item is null ? null : NormalizePrefix(item.Prefix);
            var repo = item is null ? null : NormalizeRepo(item.Repo);
            if (prefix is null || repo is null)
            {
                skipped++;
                continue;
            }

            var existing = namespaces.FirstOrDefault(x => Same(x.Prefix, prefix));
            if (existing is null) added++;
            else if (!Same(existing.Repo, repo)) updated++;
            else continue;
            namespaces = SetNamespace(namespaces, prefix, repo);
        }

        return new BindingImportResult(current with { Bindings = bindings, Namespaces = namespaces }, added, updated, skipped, null);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal sealed record ShareFile(int Version, List<ShareBinding>? Bindings, List<ShareNamespace>? Namespaces);

    internal sealed record ShareBinding(string App, string Repo);

    internal sealed record ShareNamespace(string Prefix, string Repo);
}
