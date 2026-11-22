using Cshubben.Core.Features.Settings;
using Xunit;

namespace Cshubben.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cshubben-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string File1 => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new FileSettingsStore(File1).Load();
        Assert.True(s.Notifications);
        Assert.Equal(ThemeChoice.FollowSystem, s.Theme);
        Assert.Equal(60, s.RefreshSeconds);
        Assert.Empty(s.MutedRepos);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new FileSettingsStore(File1);
        store.Save(new AppSettings
        {
            FollowedRepos = new[] { "acme/web" },
            MutedRepos = new[] { "acme/old" },
            MutedOrgs = new[] { "noisy" },
            Notifications = false,
            ClaudeModel = "opus",
            ClaudeEffort = "high",
            Theme = "dark",
            CompactLists = true,
            SidebarCollapsed = true,
            RefreshSeconds = 120,
            NotesDirectory = "D:/notes",
        });

        var s = new FileSettingsStore(File1).Load();
        Assert.Equal(new[] { "acme/web" }, s.FollowedRepos.ToArray());
        Assert.Equal(new[] { "acme/old" }, s.MutedRepos.ToArray());
        Assert.Equal(new[] { "noisy" }, s.MutedOrgs.ToArray());
        Assert.False(s.Notifications);
        Assert.Equal("opus", s.ClaudeModel);
        Assert.Equal("high", s.ClaudeEffort);
        Assert.Equal("dark", s.Theme);
        Assert.True(s.CompactLists);
        Assert.True(s.SidebarCollapsed);
        Assert.Null(new AppSettings().SidebarCollapsed);
        Assert.Equal(120, s.RefreshSeconds);
        Assert.Equal("D:/notes", s.NotesDirectory);
        Assert.False(File.Exists(File1 + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, "{ not json ]]");
        var s = new FileSettingsStore(File1).Load();
        Assert.Equal(new AppSettings().Theme, s.Theme);
        Assert.True(s.Notifications);
    }

    [Fact]
    public void Empty_file_and_null_json_fall_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, "");
        Assert.Equal(60, new FileSettingsStore(File1).Load().RefreshSeconds);
        File.WriteAllText(File1, "null");
        Assert.Equal(60, new FileSettingsStore(File1).Load().RefreshSeconds);
    }

    [Fact]
    public void Partial_file_keeps_defaults_for_missing_fields()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, "{ \"theme\": \"light\" }");
        var s = new FileSettingsStore(File1).Load();
        Assert.Equal("light", s.Theme);
        Assert.True(s.Notifications);
        Assert.Equal(60, s.RefreshSeconds);
    }

    [Fact]
    public void Unknown_theme_and_bad_interval_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, "{ \"theme\": \"neon\", \"refreshSeconds\": 1, \"mutedRepos\": [\"bad name\", \"a/b\", \"A/B\"] }");
        var s = new FileSettingsStore(File1).Load();
        Assert.Equal(ThemeChoice.FollowSystem, s.Theme);
        Assert.Equal(60, s.RefreshSeconds);
        Assert.Equal(new[] { "a/b" }, s.MutedRepos.ToArray());
    }

    [Fact]
    public void Save_creates_the_directory()
    {
        var store = new FileSettingsStore(Path.Combine(_dir, "deep", "settings.json"));
        store.Save(new AppSettings { Theme = "dark" });
        Assert.Equal("dark", store.Load().Theme);
    }

    [Fact]
    public void In_memory_store_round_trips()
    {
        var store = new InMemorySettingsStore();
        store.Save(new AppSettings { Theme = "light" });
        Assert.Equal("light", store.Load().Theme);
    }
}

public class RepoListTests
{
    [Theory]
    [InlineData("acme/web", "acme/web")]
    [InlineData("  acme/web  ", "acme/web")]
    [InlineData("https://github.com/acme/web", "acme/web")]
    [InlineData("https://github.com/acme/web.git", "acme/web")]
    [InlineData("https://github.com/acme/web/pull/3", "acme/web")]
    [InlineData("acme", "acme")]
    public void Normalize_accepts_valid_names(string input, string expected) =>
        Assert.Equal(expected, RepoList.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a b/c")]
    [InlineData("-bad/name")]
    public void Normalize_rejects_invalid_names(string input) => Assert.Null(RepoList.Normalize(input));

    [Fact]
    public void Add_dedupes_case_insensitively_and_sorts()
    {
        IReadOnlyList<string> list = new[] { "b/two" };
        list = RepoList.Add(list, "A/one");
        list = RepoList.Add(list, "B/TWO");
        list = RepoList.Add(list, "not valid");
        Assert.Equal(new[] { "A/one", "b/two" }, list.ToArray());
    }

    [Fact]
    public void Remove_ignores_case()
    {
        var list = RepoList.Remove(new[] { "a/one", "b/two" }, "A/ONE");
        Assert.Equal(new[] { "b/two" }, list.ToArray());
    }

    [Fact]
    public void IsMuted_checks_repo_and_owner()
    {
        var s = new AppSettings { MutedRepos = new[] { "acme/old" }, MutedOrgs = new[] { "noisy" } };
        Assert.True(RepoList.IsMuted(s, "ACME/old"));
        Assert.True(RepoList.IsMuted(s, "noisy/anything"));
        Assert.False(RepoList.IsMuted(s, "acme/new"));
    }
}
