using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// In-memory results of the slower page loads, so changing page shows the last result at once and reloads in the background
/// only when it is older than its time to live. Cleared by each page's Refresh button and after its own writes.
/// </summary>
static class PageCaches
{
    public static readonly StaleCache<LinearBoard> Linear = new();
    public static readonly StaleCache<DriftReport> Drift = new();
    public static readonly StaleCache<StatsInput> Stats = new();
    public static readonly StaleCache<LinearIssue> Issues = new();
    public static readonly StaleCache<TeamReport> Team = new();

    public static readonly TimeSpan LinearTtl = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DriftTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IssueTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(30);
}
