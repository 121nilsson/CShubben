using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Settings;
using Xunit;

namespace Cshubben.Core.Tests.Features.Settings;

public class RepoDirectoryTests
{
    private static readonly Dictionary<string, int> Activity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["acme/web"] = 5,
        ["acme/api"] = 9,
        ["other/tool"] = 2,
    };

    [Fact]
    public void Groups_by_owner_busiest_first()
    {
        var m = RepoDirectory.Build(Activity, new AppSettings());
        Assert.Equal(new[] { "acme", "other" }, m.Orgs.Select(o => o.Owner).ToArray());
        Assert.Equal(new[] { "api", "web" }, m.Orgs[0].Repos.Select(r => r.Name).ToArray());
        Assert.Equal(14, m.Orgs[0].Activity);
        Assert.Equal(3, m.TotalRepos);
        Assert.Equal(3, m.WatchedRepos);
    }

    [Fact]
    public void Muted_repo_stays_listed_so_it_can_be_turned_back_on()
    {
        var s = new AppSettings { MutedRepos = new[] { "acme/web", "gone/old" } };
        var m = RepoDirectory.Build(Activity, s);
        Assert.Equal(4, m.TotalRepos);
        Assert.Equal(2, m.WatchedRepos);
        Assert.False(m.Orgs.First(o => o.Owner == "acme").Repos.First(r => r.Name == "web").Watched);
        Assert.Equal(0, m.Orgs.First(o => o.Owner == "gone").Repos[0].Activity);
    }

    [Fact]
    public void Muted_owner_makes_all_its_repos_unwatched_and_an_unseen_owner_still_shows()
    {
        var s = new AppSettings { MutedOrgs = new[] { "acme", "ghost" } };
        var m = RepoDirectory.Build(Activity, s);
        Assert.All(m.Orgs.First(o => o.Owner == "acme").Repos, r => Assert.False(r.Watched));
        Assert.False(m.Orgs.First(o => o.Owner == "acme").Watched);
        Assert.Contains(m.Orgs, o => o.Owner == "ghost" && o.Repos.Count == 0);
        Assert.Equal(1, m.WatchedRepos);
    }

    [Fact]
    public void Filter_matches_owner_or_repo_and_is_case_insensitive()
    {
        var byOwner = RepoDirectory.Build(Activity, new AppSettings(), "ACME");
        Assert.Single(byOwner.Orgs);
        Assert.Equal(2, byOwner.Orgs[0].Repos.Count);

        var byRepo = RepoDirectory.Build(Activity, new AppSettings(), "tool");
        Assert.Single(byRepo.Orgs);
        Assert.Equal("other/tool", byRepo.Orgs[0].Repos[0].Repo);

        var onlyMatchingRepo = RepoDirectory.Build(Activity, new AppSettings(), "web");
        Assert.Equal(new[] { "web" }, onlyMatchingRepo.Orgs[0].Repos.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Toggles_change_the_right_list()
    {
        var s = new AppSettings();
        s = RepoDirectory.SetRepoWatched(s, "acme/web", false);
        Assert.Equal(new[] { "acme/web" }, s.MutedRepos.ToArray());
        s = RepoDirectory.SetRepoWatched(s, "ACME/Web", true);
        Assert.Empty(s.MutedRepos);

        s = RepoDirectory.SetOwnerWatched(s, "acme", false);
        Assert.Equal(new[] { "acme" }, s.MutedOrgs.ToArray());
        s = RepoDirectory.SetOwnerWatched(s, "acme", true);
        Assert.Empty(s.MutedOrgs);

        s = RepoDirectory.SetImportant(s, "acme/api", true);
        Assert.Equal(new[] { "acme/api" }, s.ImportantRepos.ToArray());
        Assert.True(RepoDirectory.Build(Activity, s).Orgs[0].Repos.First(r => r.Name == "api").Important);
        s = RepoDirectory.SetImportant(s, "acme/api", false);
        Assert.Empty(s.ImportantRepos);
    }

    [Fact]
    public void Merge_keeps_repos_missing_from_the_latest_load()
    {
        var merged = RepoDirectory.Merge(
            new Dictionary<string, int> { ["a/one"] = 3, ["a/two"] = 1 },
            new Dictionary<string, int> { ["a/one"] = 7 });
        Assert.Equal(7, merged["a/one"]);
        Assert.Equal(1, merged["a/two"]);
    }

    [Fact]
    public void Important_repos_are_listed_first_kept_over_the_cap_and_never_when_muted()
    {
        var picked = new[] { "a/one", "a/two", "b/three" };
        var result = RepoSelection.WithImportant(picked, new[] { "b/three", "z/far", "m/muted", "bad" }, new[] { "m" });
        Assert.Equal(new[] { "b/three", "z/far", "a/one", "a/two" }, result.ToArray());
    }

    [Fact]
    public void Important_repos_survive_sanitizing()
    {
        var s = new AppSettings { ImportantRepos = new[] { "https://github.com/acme/api", "bad name", "acme/api" } }.Sanitized();
        Assert.Equal(new[] { "acme/api" }, s.ImportantRepos.ToArray());
    }
}
