using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.GitHub;
using Xunit;

namespace Cshubben.Core.Tests.Features.GitHub;

public class SuggestionCommitsTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Parses_head_with_branch_and_repository()
    {
        var head = SuggestionCommits.ParseHead(Json("{\"head\":{\"sha\":\"" + Sha + "\",\"ref\":\"feature/x\",\"repo\":{\"full_name\":\"me/fork\"}}}"));
        Assert.NotNull(head);
        Assert.Equal(Sha, head!.Sha);
        Assert.Equal("feature/x", head.Ref);
        Assert.Equal("me/fork", head.RepoFullName);
    }

    [Fact]
    public void Head_without_sha_or_branch_is_rejected_and_a_deleted_fork_has_no_repo()
    {
        Assert.Null(SuggestionCommits.ParseHead(Json("{\"head\":{\"sha\":\"nothex\",\"ref\":\"x\"}}")));
        Assert.Null(SuggestionCommits.ParseHead(Json("{\"head\":{\"sha\":\"" + Sha + "\"}}")));
        Assert.Null(SuggestionCommits.ParseHead(null));
        var gone = SuggestionCommits.ParseHead(Json("{\"head\":{\"sha\":\"" + Sha + "\",\"ref\":\"x\",\"repo\":null}}"));
        Assert.Null(gone!.RepoFullName);
    }

    [Fact]
    public void Commit_request_puts_base64_content_with_blob_sha_and_branch()
    {
        var request = SuggestionCommits.CommitFileRequest("me/repo", "src/My File.cs", "feature/x", "Apply it", "héllo\n", Sha);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.github.com/repos/me/repo/contents/src/My%20File.cs", request.Url);
        using var doc = JsonDocument.Parse(request.JsonBody!);
        var root = doc.RootElement;
        Assert.Equal("Apply it", root.GetProperty("message").GetString());
        Assert.Equal("feature/x", root.GetProperty("branch").GetString());
        Assert.Equal(Sha, root.GetProperty("sha").GetString());
        Assert.Equal("héllo\n", Encoding.UTF8.GetString(Convert.FromBase64String(root.GetProperty("content").GetString()!)));
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/abs/path")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void Unsafe_paths_are_refused(string path)
    {
        Assert.False(SuggestionCommits.IsSafePath(path));
        Assert.Throws<ArgumentException>(() => SuggestionCommits.CommitFileRequest("me/repo", path, "main", "m", "x", Sha));
    }

    [Fact]
    public void Bad_repo_branch_blob_or_message_are_refused()
    {
        Assert.Throws<ArgumentException>(() => SuggestionCommits.CommitFileRequest("not a repo", "a.cs", "main", "m", "x", Sha));
        Assert.Throws<ArgumentException>(() => SuggestionCommits.CommitFileRequest("me/repo", "a.cs", "bad..branch", "m", "x", Sha));
        Assert.Throws<ArgumentException>(() => SuggestionCommits.CommitFileRequest("me/repo", "a.cs", "main", "m", "x", "zzz"));
        Assert.Throws<ArgumentException>(() => SuggestionCommits.CommitFileRequest("me/repo", "a.cs", "main", " ", "x", Sha));
    }

    [Fact]
    public void Reads_a_base64_file_and_ignores_anything_else()
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("a\nb\n"));
        var withBreaks = content.Insert(2, "\\n");
        var file = SuggestionCommits.ParseFile(Json("{\"encoding\":\"base64\",\"sha\":\"" + Sha + "\",\"content\":\"" + withBreaks + "\"}"));
        Assert.Equal("a\nb\n", file!.Value.Text);
        Assert.Equal(Sha, file.Value.BlobSha);
        Assert.Null(SuggestionCommits.ParseFile(Json("{\"encoding\":\"none\",\"sha\":\"" + Sha + "\",\"content\":\"\"}")));
        Assert.Null(SuggestionCommits.ParseFile(Json("{\"encoding\":\"base64\",\"sha\":\"" + Sha + "\",\"content\":\"***\"}")));
    }

    [Fact]
    public void Read_request_is_a_get_at_the_head_commit()
    {
        var request = SuggestionCommits.ReadFileRequest("me/repo", "a/b.cs", Sha);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/me/repo/contents/a/b.cs?ref=" + Sha, request.Url);
        Assert.Null(request.JsonBody);
    }
}
