using System.Text;
using System.Text.Json;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>Where a pull request lives: the branch to commit to, possibly in a fork.</summary>
public sealed record PullRequestHead(string Sha, string Ref, string? RepoFullName);

/// <summary>Pure builders and parsers for committing a suggested change to the pull request branch (REST contents API).</summary>
public static class SuggestionCommits
{
    /// <summary>Reads head.sha, head.ref and head.repo.full_name from a pull request answer; null when unusable.</summary>
    public static PullRequestHead? ParseHead(JsonElement? pullRequest)
    {
        var sha = J.StrOrNull(pullRequest, "head", "sha");
        var branch = J.StrOrNull(pullRequest, "head", "ref");
        if (!GitHubNames.IsCommitSha(sha) || string.IsNullOrEmpty(branch)) return null;
        return new PullRequestHead(sha!, branch, J.StrOrNull(pullRequest, "head", "repo", "full_name"));
    }

    /// <summary>GET of a file at a commit, to learn its blob sha and current text.</summary>
    public static WriteRequest ReadFileRequest(string repo, string path, string sha)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        if (!GitHubNames.IsCommitSha(sha)) throw new ArgumentException("Invalid commit sha.", nameof(sha));
        return new WriteRequest(HttpMethod.Get, ContentsUrl(owner, name, path) + "?ref=" + sha, null);
    }

    /// <summary>Decodes the text of a contents answer; null for anything but a base64 file.</summary>
    public static (string Text, string BlobSha)? ParseFile(JsonElement? contents)
    {
        if (J.Str(contents, "encoding") != "base64") return null;
        var blob = J.Str(contents, "sha");
        if (!GitHubNames.IsCommitSha(blob)) return null;
        var raw = J.Str(contents, "content").Replace("\n", string.Empty).Replace("\r", string.Empty);
        try
        {
            return (Encoding.UTF8.GetString(Convert.FromBase64String(raw)), blob);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// PUT that commits new file text to a branch. The blob sha makes GitHub refuse (409) when the file changed in the
    /// meantime, so a suggestion can never overwrite newer work.
    /// </summary>
    public static WriteRequest CommitFileRequest(string repo, string path, string branch, string message, string newContent, string blobSha)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        if (!IsSafeBranch(branch)) throw new ArgumentException("Invalid branch name.", nameof(branch));
        if (!GitHubNames.IsCommitSha(blobSha)) throw new ArgumentException("Invalid blob sha.", nameof(blobSha));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A commit message is required.", nameof(message));
        var json = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(newContent)),
            ["sha"] = blobSha,
            ["branch"] = branch,
        };
        return new WriteRequest(HttpMethod.Put, ContentsUrl(owner, name, path), JsonSerializer.Serialize(json));
    }

    /// <summary>Repository paths: relative, no empty or dot segments, no backslashes or control characters.</summary>
    public static bool IsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1000 || path.StartsWith('/') || path.EndsWith('/')) return false;
        foreach (var c in path)
            if (c < ' ' || c == '\\') return false;
        foreach (var segment in path.Split('/'))
            if (segment.Length == 0 || segment == "." || segment == "..") return false;
        return true;
    }

    private static bool IsSafeBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch) || branch.Length > 250 || branch.StartsWith('-') || branch.Contains("..", StringComparison.Ordinal)) return false;
        foreach (var c in branch)
            if (c < ' ' || c == '\\' || c == ' ' || c == '~' || c == '^' || c == ':' || c == '?' || c == '*' || c == '[') return false;
        return true;
    }

    private static string ContentsUrl(string owner, string name, string path)
    {
        if (!IsSafePath(path)) throw new ArgumentException("Invalid file path.", nameof(path));
        var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        return GitHubRest.ApiBase + "/repos/" + owner + "/" + name + "/contents/" + escaped;
    }
}
