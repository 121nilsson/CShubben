using Cshubben.Core.Model;

namespace Cshubben.Core.Data;

/// <summary>Source of hub data. Implemented by the demo data and (later) the GitHub client.</summary>
public interface IHubData
{
    Task<HubSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}
