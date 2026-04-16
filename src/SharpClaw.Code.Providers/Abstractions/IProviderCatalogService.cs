using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Abstractions;

/// <summary>
/// Resolves the surfaced provider/model catalog, including local runtime profiles.
/// </summary>
public interface IProviderCatalogService
{
    /// <summary>
    /// Lists the effective provider model catalog.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog entries.</returns>
    Task<IReadOnlyList<ProviderModelCatalogEntry>> ListAsync(CancellationToken cancellationToken);
}
