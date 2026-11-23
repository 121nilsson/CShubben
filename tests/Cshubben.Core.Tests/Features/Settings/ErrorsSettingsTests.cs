using Cshubben.Core.Features.Settings;
using Xunit;

namespace Cshubben.Core.Tests.Settings;

public sealed class ErrorsSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cshubben-errors-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void The_module_is_off_by_default_and_empty()
    {
        var s = new AppSettings().Errors;
        Assert.False(s.Enabled);
        Assert.Equal("", s.GrafanaUrl);
        Assert.Equal("", s.RepoRoot);
        Assert.Equal("", s.LokiDatasource);
        Assert.Equal("default", s.Namespace);
        Assert.False(s.IncludeWarnings);
        Assert.Empty(s.Bindings);
        Assert.Empty(s.Namespaces);
    }

    [Fact]
    public void Source_and_azure_fields_are_repaired()
    {
        Assert.Equal("azure", new ErrorsSettings().Source);
        Assert.Equal("azure", new ErrorsSettings { Source = "nonsense" }.Sanitized().Source);
        Assert.Equal("azure", new ErrorsSettings { Source = null! }.Sanitized().Source);
        Assert.Equal("grafana", new ErrorsSettings { Source = " Grafana " }.Sanitized().Source);

        var s = new ErrorsSettings { McpUrl = "http://example.com/mcp", Workspace = "two words", ResourceGroup = " rg-1 " }.Sanitized();
        Assert.Equal("", s.McpUrl);
        Assert.Equal("", s.Workspace);
        Assert.Equal("rg-1", s.ResourceGroup);
        Assert.Equal("https://logs.example.com", new ErrorsSettings { McpUrl = "https://logs.example.com/" }.Sanitized().McpUrl);
    }

    [Fact]
    public void Namespace_and_datasource_are_trimmed_and_repaired()
    {
        var s = new ErrorsSettings { Namespace = "two words", LokiDatasource = "  P8E80F9AEF21F6940  " }.Sanitized();
        Assert.Equal("default", s.Namespace);
        Assert.Equal("P8E80F9AEF21F6940", s.LokiDatasource);
        Assert.Equal("prod", new ErrorsSettings { Namespace = " prod " }.Sanitized().Namespace);
        Assert.Equal("default", new ErrorsSettings { Namespace = null! }.Sanitized().Namespace);
    }

    [Fact]
    public void A_settings_file_without_errors_loads_with_the_module_off()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"theme\": \"dark\" }");
        var s = new FileSettingsStore(path).Load();
        Assert.False(s.Errors.Enabled);
        Assert.Empty(s.Errors.Bindings);
    }

    [Fact]
    public void Errors_settings_round_trip_through_the_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        new FileSettingsStore(path).Save(new AppSettings
        {
            Errors = new ErrorsSettings
            {
                Enabled = true,
                GrafanaUrl = "https://grafana.example.com/",
                RepoRoot = " D:/src ",
                Bindings = new[] { new RepoBinding("properties-api", "acme/properties-api"), new RepoBinding("binders-api", "acme/binders-api") },
                Namespaces = new[] { new NamespaceBinding("Acme.BuildingBlocks", "acme/building-blocks") },
            },
        });

        var e = new FileSettingsStore(path).Load().Errors;
        Assert.True(e.Enabled);
        Assert.Equal("https://grafana.example.com", e.GrafanaUrl);
        Assert.Equal("D:/src", e.RepoRoot);
        Assert.Equal(new[] { "binders-api", "properties-api" }, e.Bindings.Select(b => b.App).ToArray());
        Assert.Equal("acme/binders-api", e.Bindings[0].Repo);
        Assert.Equal("acme/building-blocks", Assert.Single(e.Namespaces).Repo);
    }

    [Fact]
    public void Null_lists_in_a_damaged_file_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"errors\": { \"enabled\": true, \"bindings\": null, \"namespaces\": null, \"grafanaUrl\": null } }");
        var e = new FileSettingsStore(path).Load().Errors;
        Assert.True(e.Enabled);
        Assert.Empty(e.Bindings);
        Assert.Empty(e.Namespaces);
        Assert.Equal("", e.GrafanaUrl);
    }

    [Theory]
    [InlineData("https://grafana.example.com", "https://grafana.example.com")]
    [InlineData("  https://grafana.example.com/  ", "https://grafana.example.com")]
    [InlineData("https://example.com/grafana/", "https://example.com/grafana")]
    [InlineData("https://example.com:3000/?orgId=1", "https://example.com:3000")]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000/", "http://127.0.0.1:3000")]
    public void Grafana_addresses_are_normalised(string input, string expected) =>
        Assert.Equal(expected, RepoBindings.NormalizeUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("grafana.example.com")]
    [InlineData("http://grafana.example.com")]
    [InlineData("ftp://grafana.example.com")]
    [InlineData("file:///etc/passwd")]
    public void Unsafe_or_incomplete_addresses_are_rejected(string input) =>
        Assert.Null(RepoBindings.NormalizeUrl(input));

    [Theory]
    [InlineData("binders-api", "binders-api")]
    [InlineData("  notifications-messagefactory ", "notifications-messagefactory")]
    [InlineData("api_v2.web", "api_v2.web")]
    public void App_names_accept_plain_names(string input, string expected) =>
        Assert.Equal(expected, RepoBindings.NormalizeApp(input));

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("-leading")]
    [InlineData("acme/binders-api")]
    public void App_names_reject_everything_else(string input) => Assert.Null(RepoBindings.NormalizeApp(input));

    [Theory]
    [InlineData("acme/web", "acme/web")]
    [InlineData("https://github.com/acme/web.git", "acme/web")]
    public void Repos_must_be_owner_and_name(string input, string expected) =>
        Assert.Equal(expected, RepoBindings.NormalizeRepo(input));

    [Theory]
    [InlineData("acme")]
    [InlineData("")]
    [InlineData("a b/c")]
    public void A_bare_owner_is_not_a_repo(string input) => Assert.Null(RepoBindings.NormalizeRepo(input));

    [Theory]
    [InlineData("Acme.BuildingBlocks", "Acme.BuildingBlocks")]
    [InlineData("Acme.BuildingBlocks.*", "Acme.BuildingBlocks")]
    [InlineData("Acme.BuildingBlocks.", "Acme.BuildingBlocks")]
    [InlineData("Acme", "Acme")]
    public void Namespace_prefixes_drop_wildcards(string input, string expected) =>
        Assert.Equal(expected, RepoBindings.NormalizePrefix(input));

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("Acme..Core")]
    [InlineData("1Acme")]
    [InlineData("Acme Core")]
    public void Bad_namespace_prefixes_are_rejected(string input) => Assert.Null(RepoBindings.NormalizePrefix(input));

    [Fact]
    public void Set_adds_replaces_and_sorts()
    {
        IReadOnlyList<RepoBinding> list = Array.Empty<RepoBinding>();
        list = RepoBindings.Set(list, "properties-api", "acme/properties-api");
        list = RepoBindings.Set(list, "binders-api", "acme/old-name");
        list = RepoBindings.Set(list, "BINDERS-API", "acme/binders-api");
        Assert.Equal(new[] { "BINDERS-API", "properties-api" }, list.Select(b => b.App).ToArray());
        Assert.Equal("acme/binders-api", list[0].Repo);
    }

    [Fact]
    public void Set_ignores_invalid_input()
    {
        IReadOnlyList<RepoBinding> list = new[] { new RepoBinding("a", "acme/a") };
        Assert.Same(list, RepoBindings.Set(list, "two words", "acme/b"));
        Assert.Same(list, RepoBindings.Set(list, "b", "not a repo"));
        Assert.Same(list, RepoBindings.Set(list, "b", "acme"));
    }

    [Fact]
    public void Remove_ignores_case()
    {
        IReadOnlyList<RepoBinding> list = new[] { new RepoBinding("a", "acme/a"), new RepoBinding("b", "acme/b") };
        Assert.Equal(new[] { "b" }, RepoBindings.Remove(list, "A").Select(x => x.App).ToArray());
    }

    [Fact]
    public void Clean_drops_invalid_and_duplicates_first_one_wins()
    {
        var cleaned = RepoBindings.CleanBindings(new RepoBinding?[]
        {
            new("b-api", "acme/b"), null, new("bad name", "acme/x"), new("a-api", "nope"),
            new("B-API", "acme/other"), new("a-api", "acme/a"),
        });
        Assert.Equal(new[] { "a-api", "b-api" }, cleaned.Select(b => b.App).ToArray());
        Assert.Equal("acme/b", cleaned[1].Repo);
    }

    [Fact]
    public void Namespaces_are_set_removed_and_cleaned()
    {
        IReadOnlyList<NamespaceBinding> list = Array.Empty<NamespaceBinding>();
        list = RepoBindings.SetNamespace(list, "Acme.Core.*", "acme/core");
        list = RepoBindings.SetNamespace(list, "acme.core", "acme/core2");
        list = RepoBindings.SetNamespace(list, "1bad", "acme/x");
        Assert.Equal("acme.core", Assert.Single(list).Prefix);
        Assert.Equal("acme/core2", list[0].Repo);
        Assert.Empty(RepoBindings.RemoveNamespace(list, "ACME.CORE"));
    }

    [Fact]
    public void RepoFor_finds_the_app_ignoring_case_and_returns_null_otherwise()
    {
        var s = new ErrorsSettings { Bindings = new[] { new RepoBinding("binders-api", "acme/binders-api") } };
        Assert.Equal("acme/binders-api", RepoBindings.RepoFor(s, "Binders-API"));
        Assert.Equal("acme/binders-api", RepoBindings.RepoFor(s, "  binders-api "));
        Assert.Null(RepoBindings.RepoFor(s, "properties-api"));
        Assert.Null(RepoBindings.RepoFor(s, ""));
        Assert.Null(RepoBindings.RepoFor(s, null));
    }

    [Fact]
    public void RepoForNamespace_uses_the_longest_prefix_on_whole_name_parts()
    {
        var s = new ErrorsSettings
        {
            Namespaces = new[]
            {
                new NamespaceBinding("Acme", "acme/platform"),
                new NamespaceBinding("Acme.BuildingBlocks", "acme/building-blocks"),
                new NamespaceBinding("Acme.BuildingBlocks.Infrastructure.Authorization", "acme/auth"),
            },
        };

        Assert.Equal("acme/auth", RepoBindings.RepoForNamespace(s, "Acme.BuildingBlocks.Infrastructure.Authorization.PolicyServer.PolicyServerRuntimeRepository"));
        Assert.Equal("acme/building-blocks", RepoBindings.RepoForNamespace(s, "Acme.BuildingBlocks.Core.Extensions.Async.TaskExtensions"));
        Assert.Equal("acme/platform", RepoBindings.RepoForNamespace(s, "Acme.Notifications.MessageFactory"));
        Assert.Equal("acme/building-blocks", RepoBindings.RepoForNamespace(s, "acme.buildingblocks"));
        Assert.Null(RepoBindings.RepoForNamespace(s, "AcmeOther.Thing"));
        Assert.Null(RepoBindings.RepoForNamespace(s, "System.Linq.Enumerable"));
        Assert.Null(RepoBindings.RepoForNamespace(s, ""));
    }

    [Fact]
    public void Suggest_returns_the_repo_only_when_exactly_one_matches()
    {
        var known = new[] { "acme/binders-api", "acme/properties-api", "other/shared", "acme/shared" };
        Assert.Equal("acme/binders-api", RepoBindings.Suggest("binders-api", known));
        Assert.Equal("acme/properties-api", RepoBindings.Suggest("Properties-API", known));
        Assert.Null(RepoBindings.Suggest("shared", known));
        Assert.Null(RepoBindings.Suggest("missing-api", known));
        Assert.Null(RepoBindings.Suggest("binders", known));
        Assert.Null(RepoBindings.Suggest("two words", known));
        Assert.Null(RepoBindings.Suggest("", known));
    }

    [Fact]
    public void Suggest_counts_the_same_repo_once()
    {
        Assert.Equal("acme/binders-api", RepoBindings.Suggest("binders-api", new[] { "acme/binders-api", "ACME/binders-api" }));
    }

    [Fact]
    public void Export_is_one_line_and_leaves_out_the_address_the_token_and_local_folders()
    {
        var s = new ErrorsSettings
        {
            Enabled = true,
            GrafanaUrl = "https://grafana.example.com",
            RepoRoot = "D:/secret-folder",
            Bindings = new[] { new RepoBinding("binders-api", "acme/binders-api") },
            Namespaces = new[] { new NamespaceBinding("Acme.Core", "acme/core") },
        };

        var json = RepoBindings.ToJson(s);
        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain("grafana", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-folder", json, StringComparison.Ordinal);
        Assert.Contains("binders-api", json, StringComparison.Ordinal);
        Assert.Contains("Acme.Core", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_then_import_round_trips_into_empty_settings()
    {
        var source = new ErrorsSettings
        {
            Bindings = new[] { new RepoBinding("binders-api", "acme/binders-api"), new RepoBinding("properties-api", "acme/properties-api") },
            Namespaces = new[] { new NamespaceBinding("Acme.Core", "acme/core") },
        };

        var result = RepoBindings.Import(new ErrorsSettings { Enabled = true, GrafanaUrl = "https://g.example.com" }, RepoBindings.ToJson(source));

        Assert.True(result.Ok);
        Assert.Equal(3, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(source.Bindings, result.Settings.Bindings);
        Assert.Equal(source.Namespaces, result.Settings.Namespaces);
        Assert.True(result.Settings.Enabled);
        Assert.Equal("https://g.example.com", result.Settings.GrafanaUrl);
    }

    [Fact]
    public void Import_merges_updates_changed_repos_and_skips_invalid_entries()
    {
        var current = new ErrorsSettings
        {
            Bindings = new[] { new RepoBinding("binders-api", "acme/old"), new RepoBinding("keep-api", "acme/keep") },
        };

        const string json = """
            { "version": 1,
              "bindings": [
                { "app": "binders-api", "repo": "acme/binders-api" },
                { "app": "keep-api", "repo": "acme/keep" },
                { "app": "new-api", "repo": "acme/new-api" },
                { "app": "bad name", "repo": "acme/x" },
                { "app": "no-repo", "repo": "acme" }
              ],
              "namespaces": [ { "prefix": "1bad", "repo": "acme/x" } ] }
            """;

        var result = RepoBindings.Import(current, json);

        Assert.True(result.Ok);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(3, result.Skipped);
        Assert.Equal(new[] { "binders-api", "keep-api", "new-api" }, result.Settings.Bindings.Select(b => b.App).ToArray());
        Assert.Equal("acme/binders-api", RepoBindings.RepoFor(result.Settings, "binders-api"));
        Assert.Equal("Imported 1 new and 1 changed mappings, skipped 3 invalid.", result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void Import_of_broken_input_changes_nothing_and_says_why(string json)
    {
        var current = new ErrorsSettings { Bindings = new[] { new RepoBinding("a", "acme/a") } };
        var result = RepoBindings.Import(current, json);
        Assert.False(result.Ok);
        Assert.Same(current, result.Settings);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
