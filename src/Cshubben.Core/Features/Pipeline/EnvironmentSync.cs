namespace Cshubben.Core.Features.Pipeline;

/// <summary>Whether a repository's environments run the same commit. Differences are what Drift is for; equal is the normal, quiet state.</summary>
/// <param name="Differs">True when at least two environments run different commits.</param>
/// <param name="Text">"Environments differ: test 1a2b3c4, prod 9f8e7d6" or "All 2 environments at 1a2b3c4".</param>
public sealed record EnvironmentSyncInfo(bool Differs, string Text);

public static class EnvironmentSync
{
    /// <summary>Null with fewer than two environments (nothing to compare) or when a commit is unknown.</summary>
    public static EnvironmentSyncInfo? Describe(IReadOnlyList<Deployment> environments)
    {
        ArgumentNullException.ThrowIfNull(environments);
        var latest = environments
            .Where(d => d.Sha.Length > 0)
            .GroupBy(d => d.Environment, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .OrderBy(d => d.Environment, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (latest.Count < 2) return null;

        var shas = latest.Select(d => PipelineFormat.ShortSha(d.Sha)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (shas.Count == 1)
            return new EnvironmentSyncInfo(false, "All " + latest.Count + " environments at " + shas[0]);

        return new EnvironmentSyncInfo(true,
            "Environments differ: " + string.Join(", ", latest.Select(d => d.Environment + " " + PipelineFormat.ShortSha(d.Sha))));
    }
}
