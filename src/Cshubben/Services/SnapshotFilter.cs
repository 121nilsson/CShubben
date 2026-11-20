using Cshubben.Core.Data;
using Cshubben.Core.Features.Feed;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// Applies the muted repos and organisations from settings to a snapshot before any page sees it.
/// The pure filtering lives in <see cref="RepoFilter"/> (Core, tested); this only reads the settings.
/// </summary>
static class SnapshotFilter
{
    public static HubSnapshot Apply(HubSnapshot snapshot, AppSettings settings) =>
        RepoFilter.FilterSnapshot(snapshot, settings.MutedRepos, settings.MutedOrgs);
}

/// <summary>Wraps a data source so every loaded snapshot is filtered with the current settings. Settings are read on each load.</summary>
sealed class FilteringHubData(IHubData inner, Func<AppSettings> settings) : IHubData
{
    public async Task<HubSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await inner.LoadAsync(cancellationToken).ConfigureAwait(false);
        return SnapshotFilter.Apply(snapshot, settings());
    }
}
