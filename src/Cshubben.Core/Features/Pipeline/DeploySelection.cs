namespace Cshubben.Core.Features.Pipeline;

/// <summary>Chooses the deployment that was running at a point in time, for example when an error first showed up.</summary>
public static class DeploySelection
{
    /// <summary>
    /// The newest successful deployment created at or before <paramref name="at"/>. Deployments must have their status filled in.
    /// Null when there is none, for example for a shared library that is never deployed on its own.
    /// </summary>
    public static Deployment? Pick(IEnumerable<Deployment> deployments, DateTimeOffset at) =>
        deployments
            .Where(d => d.State == DeployState.Success && PipelineJson.IsSha(d.Sha) && d.CreatedAt <= at)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();
}
