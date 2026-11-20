using Cshubben.Core.Shared;
using Cshubben.Core.Features.Claude;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class BriefingParserTests
{
    [Fact]
    public void Parses_json_inside_a_markdown_fence_with_prose_around_it()
    {
        var text = "Here you go:\n```json\n{\"headline\":\"Two PRs need you\",\"summary\":\"CI is red on one.\"," +
                   "\"items\":[{\"kind\":\"action\",\"text\":\"Fix CI\",\"prUrl\":\"https://github.com/acme/api/pull/9\"}," +
                   "{\"kind\":\"watch\",\"text\":\"Big change\"}]}\n```\nHope that helps.";
        var result = BriefingParser.Parse(text);
        Assert.NotNull(result);
        Assert.Equal("Two PRs need you", result!.Headline);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(BriefingItemKind.Action, result.Items[0].Kind);
        Assert.Equal("https://github.com/acme/api/pull/9", result.Items[0].PrUrl);
        Assert.Null(result.Items[1].PrUrl);
        Assert.Equal(1, result.ActionCount);
    }

    [Theory]
    [InlineData("https://evil.example/pull/1")]
    [InlineData("http://github.com/acme/api/pull/1")]
    [InlineData("https://github.com.evil.example/a")]
    [InlineData("https://github.com@evil.example/a")]
    [InlineData("javascript:alert(1)")]
    public void Non_github_links_are_dropped(string url)
    {
        var json = "{\"headline\":\"h\",\"summary\":\"s\",\"items\":[{\"kind\":\"info\",\"text\":\"t\",\"prUrl\":\"" + url + "\"}]}";
        var result = BriefingParser.Parse(json);
        Assert.Null(result!.Items[0].PrUrl);
    }

    [Fact]
    public void Links_inside_text_are_filtered_too()
    {
        var json = "{\"headline\":\"h\",\"summary\":\"Visit https://evil.example/x or https://github.com/acme/api\",\"items\":[]}";
        var result = BriefingParser.Parse(json)!;
        Assert.DoesNotContain("evil.example", result.Summary);
        Assert.Contains("https://github.com/acme/api", result.Summary);
    }

    [Fact]
    public void Caps_items_and_cleans_them()
    {
        var items = string.Join(",", Enumerable.Range(0, 12).Select(i => "{\"kind\":\"weird\",\"text\":\"item " + i + "\"}"));
        var json = "{\"headline\":\"h\",\"summary\":\"s\",\"items\":[" + items + ",{\"kind\":\"action\",\"text\":\"  \"}]}";
        var result = BriefingParser.Parse(json)!;
        Assert.Equal(BriefingParser.MaxItems, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(BriefingItemKind.Info, i.Kind));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{broken")]
    public void Invalid_input_gives_null(string text)
    {
        Assert.Null(BriefingParser.Parse(text));
    }
}

public class IssueDraftParserTests
{
    [Fact]
    public void Parses_a_full_draft()
    {
        var json = "{\"title\":\"Fix cache invalidation\",\"description\":\"## Why\\nStale data.\",\"priority\":2,\"labels\":[\"bug\",\"Bug\",\"backend\",\"\"]}";
        var draft = IssueDraftParser.Parse(json);
        Assert.NotNull(draft);
        Assert.Equal("Fix cache invalidation", draft!.Title);
        Assert.Contains("Stale data.", draft.Description);
        Assert.Equal(2, draft.Priority);
        Assert.Equal(new[] { "bug", "backend" }, draft.Labels);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("-1")]
    [InlineData("\"high\"")]
    [InlineData("null")]
    public void Out_of_range_priority_becomes_null(string priority)
    {
        var draft = IssueDraftParser.Parse("{\"title\":\"t\",\"priority\":" + priority + "}");
        Assert.Null(draft!.Priority);
    }

    [Fact]
    public void Priority_given_as_a_string_digit_is_accepted()
    {
        Assert.Equal(3, IssueDraftParser.Parse("{\"title\":\"t\",\"priority\":\"3\"}")!.Priority);
    }

    [Fact]
    public void Missing_title_gives_null_and_labels_are_capped()
    {
        Assert.Null(IssueDraftParser.Parse("{\"description\":\"x\"}"));
        var labels = string.Join(",", Enumerable.Range(0, 10).Select(i => "\"l" + i + "\""));
        var draft = IssueDraftParser.Parse("{\"title\":\"t\",\"labels\":[" + labels + "]}");
        Assert.Equal(IssueDraftParser.MaxLabels, draft!.Labels.Count);
    }

    [Fact]
    public void Description_links_are_filtered()
    {
        var draft = IssueDraftParser.Parse("{\"title\":\"t\",\"description\":\"see https://evil.example/x\"}");
        Assert.DoesNotContain("evil.example", draft!.Description);
    }
}

public class LinkFilterTests
{
    [Theory]
    [InlineData("https://github.com/acme/api/pull/1", true)]
    [InlineData("https://github.com/", true)]
    [InlineData("http://github.com/a", false)]
    [InlineData("https://gist.github.com/a", false)]
    [InlineData("https://github.com.evil.example/a", false)]
    [InlineData("", false)]
    public void Recognises_github_urls(string url, bool expected)
    {
        Assert.Equal(expected, LinkFilter.IsGitHubUrl(url));
    }

    [Fact]
    public void Strip_replaces_only_foreign_links()
    {
        var text = "a https://evil.example/x b [x](https://github.com/o/r) c";
        var stripped = LinkFilter.StripNonGitHubLinks(text);
        Assert.DoesNotContain("evil.example", stripped);
        Assert.Contains("https://github.com/o/r", stripped);
    }
}

public class SecretStoreTests
{
    [Fact]
    public void Stores_overwrites_and_deletes()
    {
        var store = new InMemorySecretStore();
        Assert.Null(store.Get("k"));
        store.Set("k", "v1");
        store.Set("k", "v2");
        Assert.Equal("v2", store.Get("k"));
        store.Delete("k");
        Assert.Null(store.Get("k"));
        store.Delete("k");
    }

    [Fact]
    public void Empty_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new InMemorySecretStore().Set("", "v"));
    }
}
