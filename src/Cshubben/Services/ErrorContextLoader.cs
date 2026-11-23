using System.Globalization;
using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Pipeline;

namespace Cshubben.Services;

/// <summary>
/// Reads what <see cref="ErrorCodeContext"/> needs from GitHub: which commit was deployed (the deployments of the repository for
/// the environment, so a manual run from a pull request branch counts), the source file at that commit and on the default branch,
/// and what changed between them. Reads only. This is app-layer code because it joins the Errors and Pipeline features.
/// </summary>
sealed class ErrorContextLoader
{
    private const int MaxStatusLookups = 6;
    private readonly GitHubRest _rest;

    public ErrorContextLoader(GitHubRest rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        _rest = rest;
    }

    public async Task<ErrorCodeContext> LoadAsync(
        string repo, string? file, string environment, DateTimeOffset firstSeen, CancellationToken cancellationToken = default)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        var baseUrl = GitHubRest.ApiBase + "/repos/" + owner + "/" + name;

        var info = await GetOptionalAsync(baseUrl, cancellationToken).ConfigureAwait(false);
        var branch = PipelineJson.DefaultBranchOf(info) ?? "main";

        var deployments = await GetOptionalAsync(
            baseUrl + "/deployments?per_page=30&environment=" + Uri.EscapeDataString(environment), cancellationToken).ConfigureAwait(false);
        var candidates = PipelineJson.ParseDeployments(deployments)
            .Where(d => d.CreatedAt <= firstSeen)
            .Take(MaxStatusLookups)
            .ToList();
        var statuses = await Task.WhenAll(candidates.Select(d => GetOptionalAsync(
            baseUrl + "/deployments/" + d.Id.ToString(CultureInfo.InvariantCulture) + "/statuses?per_page=1", cancellationToken))).ConfigureAwait(false);
        var picked = DeploySelection.Pick(candidates.Select((d, i) => PipelineJson.WithStatus(d, statuses[i])), firstSeen);

        if (picked is null)
        {
            var (path, current) = await ReadFileAsync(baseUrl, file, branch, cancellationToken).ConfigureAwait(false);
            return new ErrorCodeContext(repo, branch, environment, null, null,
                "No successful deployment to '" + environment + "' was found before the error first appeared (a shared library is never deployed on its own, "
                + "and a manual deployment outside GitHub leaves no record). The code below is the default branch as it is now, "
                + "which may not be what was running.",
                path, null, current, null, null, Array.Empty<string>(), null);
        }

        var deployed = new DeployedVersion(environment, picked.Sha, picked.Ref, picked.Creator, picked.CreatedAt);
        var deployedFile = ReadFileAsync(baseUrl, file, picked.Sha, cancellationToken);
        var currentFile = ReadFileAsync(baseUrl, file, branch, cancellationToken);
        var compare = GetOptionalAsync(baseUrl + "/compare/" + picked.Sha + "..." + Uri.EscapeDataString(branch), cancellationToken);
        var pulls = GetOptionalAsync(baseUrl + "/commits/" + picked.Sha + "/pulls", cancellationToken);
        await Task.WhenAll(deployedFile, currentFile, compare, pulls).ConfigureAwait(false);

        var (deployedPath, atDeployed) = await deployedFile.ConfigureAwait(false);
        var (currentPath, atDefault) = await currentFile.ConfigureAwait(false);
        var path2 = deployedPath ?? currentPath;
        var cmp = await compare.ConfigureAwait(false);

        var commits = Items(cmp, "commits")
            .Select(c => (Str(c, "sha") ?? "").PadRight(7)[..7] + " " + FirstLine(Str(Obj(c, "commit"), "message")))
            .ToList();
        var shownCommits = commits.Skip(Math.Max(0, commits.Count - 12)).ToList();

        string? patch = null;
        bool? changed = null;
        if (cmp is not null && path2 is not null)
        {
            var match = Items(cmp, "files").FirstOrDefault(f => string.Equals(Str(f, "filename"), path2, StringComparison.Ordinal));
            changed = match.ValueKind == JsonValueKind.Object;
            if (changed == true) patch = Str(match, "patch");
        }

        var pr = Items(await pulls.ConfigureAwait(false), null).FirstOrDefault();
        var prText = pr.ValueKind == JsonValueKind.Object
            ? "#" + Num(pr, "number") + " " + Str(pr, "title") + " (" + (Str(pr, "merged_at") is not null ? "merged" : Str(pr, "state")) + ")"
            : null;

        var onDefault = string.Equals(picked.Ref, branch, StringComparison.Ordinal);
        var note = onDefault
            ? "Deployed to '" + environment + "' from the default branch (" + branch + ") at " + picked.Sha[..7] + "."
            : "Deployed to '" + environment + "' from '" + picked.Ref + "', not from the default branch (a manual run or a pull request branch), at " + picked.Sha[..7] + ".";

        return new ErrorCodeContext(repo, branch, environment, deployed, prText, note, path2, atDeployed, atDefault,
            changed, PipelineJson.AheadBy(cmp), shownCommits, patch);
    }

    /// <summary>
    /// Reads the file at a commit or branch. Build paths lose their leading "src/" in the logs, so it tries "src/path" first, then "path".
    /// </summary>
    private async Task<(string? Path, string? Text)> ReadFileAsync(string baseUrl, string? file, string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file)) return (null, null);
        foreach (var path in new[] { "src/" + file, file })
        {
            var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            var answer = await GetOptionalAsync(baseUrl + "/contents/" + escaped + "?ref=" + Uri.EscapeDataString(reference), ct).ConfigureAwait(false);
            if (answer is not { } json || Str(json, "encoding") != "base64") continue;
            try
            {
                var raw = (Str(json, "content") ?? "").Replace("\n", "").Replace("\r", "");
                return (path, Encoding.UTF8.GetString(Convert.FromBase64String(raw)));
            }
            catch (FormatException)
            {
                // Try the next candidate.
            }
        }

        return (null, null);
    }

    private async Task<JsonElement?> GetOptionalAsync(string url, CancellationToken ct)
    {
        try
        {
            return (await _rest.GetAsync(url, null, ct).ConfigureAwait(false)).Json;
        }
        catch (GitHubApiException ex) when (!ex.IsRateLimited)
        {
            return null;
        }
    }

    private static string FirstLine(string? text)
    {
        var line = (text ?? "").Replace("\r", "").Split('\n')[0].Trim();
        return line.Length <= 100 ? line : line[..99] + "…";
    }

    // Small tolerant JSON helpers: a missing or mistyped value gives null instead of an exception.

    private static string? Str(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static long? Num(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    /// <summary>The array property <paramref name="name"/>, or the element itself when it is an array and no name is given.</summary>
    private static List<JsonElement> Items(JsonElement? element, string? name)
    {
        var result = new List<JsonElement>();
        if (element is not { } e) return result;
        if (name is not null)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e)) return result;
        }

        if (e.ValueKind == JsonValueKind.Array) result.AddRange(e.EnumerateArray());
        return result;
    }
}
