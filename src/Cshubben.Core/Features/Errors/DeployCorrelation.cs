namespace Cshubben.Core.Features.Errors;

/// <summary>A deployment and how long before something it happened.</summary>
/// <param name="Deploy">The deployment.</param>
/// <param name="After">How long after the deployment the thing started (never negative).</param>
public sealed record DeployLink(DeployedVersion Deploy, TimeSpan After);

/// <summary>
/// Ties an error spike, or an error that showed up for the first time, to the deployment that probably caused it:
/// the newest deployment shortly before it started. Pure, so every rule is tested.
/// </summary>
public static class DeployCorrelation
{
    /// <summary>How long before a spike a deployment still counts as its probable cause.</summary>
    public static readonly TimeSpan SpikeWindow = TimeSpan.FromMinutes(90);

    /// <summary>How long before a new error a deployment still counts as its probable cause.</summary>
    public static readonly TimeSpan NewErrorWindow = TimeSpan.FromHours(2);

    /// <summary>The newest deployment at or before <paramref name="at"/>; null when there is none.</summary>
    public static DeployedVersion? Before(IEnumerable<DeployedVersion> deploys, DateTimeOffset at) =>
        deploys
            .Where(d => d.DeployedAt <= at)
            .OrderByDescending(d => d.DeployedAt)
            .FirstOrDefault();

    /// <summary>
    /// The newest deployment at most <paramref name="within"/> before <paramref name="at"/>. A thing that starts in the same bucket
    /// as a deployment looks as if it started a little before it, so <paramref name="tolerance"/> (one bucket) is allowed.
    /// </summary>
    public static DeployLink? Link(IEnumerable<DeployedVersion> deploys, DateTimeOffset at, TimeSpan within, TimeSpan tolerance = default)
    {
        var deploy = Before(deploys, at + tolerance);
        if (deploy is null) return null;

        var gap = at - deploy.DeployedAt;
        if (gap < TimeSpan.Zero) gap = TimeSpan.Zero;
        return gap <= within ? new DeployLink(deploy, gap) : null;
    }

    /// <summary>
    /// The deployment an error group first showed up after. Null when the group was already there when the window began
    /// (it did not start with a deployment), when no deployment came shortly before it, and when <paramref name="complete"/> is false:
    /// then older log lines were cut off (see <see cref="GroupSampling"/>), so the first line that was read is not the first one.
    /// </summary>
    public static DeployLink? FirstSeenLink(IEnumerable<DeployedVersion> deploys, ErrorGroup group, ErrorWindow window, bool complete = true)
    {
        if (!complete) return null;
        if (group.FirstSeen < window.Start + TimeSpan.FromHours(1)) return null;
        return Link(deploys, group.FirstSeen, NewErrorWindow, window.Step);
    }
}

/// <summary>Finds when a spike began.</summary>
public static class SpikeOnset
{
    /// <summary>Buckets without elevated errors that still do not end the spike (a quiet bucket in the middle is noise).</summary>
    private const int MaxGap = 2;

    /// <summary>
    /// The start of the bucket where the current spike began: the first bucket of the run of elevated buckets that reaches the
    /// latest hour. Elevated means above the normal level of the application (median of its history plus one robust deviation);
    /// the run must also contain a bucket above the spike threshold. Null when the latest hour is not elevated.
    /// </summary>
    public static DateTimeOffset? Find(IReadOnlyList<int> counts, ErrorWindow window, SpikeOptions? options = null)
    {
        var o = options ?? new SpikeOptions();
        var n = Math.Min(counts.Count, window.Buckets);
        var nowBuckets = Math.Clamp(o.NowBuckets, 1, Math.Max(n, 1));
        var historyLength = n - nowBuckets;
        if (n < 2 || historyLength < 1) return null;

        var history = new List<double>(historyLength);
        for (var i = 0; i < historyLength; i++) history.Add(counts[i]);
        var baseline = SpikeDetector.Median(history);
        var deviations = new List<double>(historyLength);
        foreach (var value in history) deviations.Add(Math.Abs(value - baseline));
        var deviation = Math.Max(SpikeDetector.Median(deviations) * SpikeDetector.MadToSigma, SpikeDetector.MinDeviation);

        var elevated = baseline + deviation;
        var strict = baseline + o.K * deviation;

        var last = -1;
        for (var i = n - 1; i >= n - nowBuckets; i--)
        {
            if (counts[i] <= elevated) continue;
            last = i;
            break;
        }

        if (last < 0) return null;

        var start = last;
        var gap = 0;
        for (var i = last - 1; i >= 0; i--)
        {
            if (counts[i] > elevated)
            {
                start = i;
                gap = 0;
            }
            else if (++gap > MaxGap)
            {
                break;
            }
        }

        var reachesThreshold = false;
        for (var i = start; i <= last; i++)
        {
            if (counts[i] <= strict) continue;
            reachesThreshold = true;
            break;
        }

        return reachesThreshold ? window.Start + TimeSpan.FromTicks(window.Step.Ticks * start) : null;
    }
}
