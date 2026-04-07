using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Abstractions;

/// <summary>
/// Normalizes provider requests before execution.
/// </summary>
public interface IProviderRequestPreflight
{
    /// <summary>
    /// Resolves provider and model aliases into a normalized provider request.
    /// </summary>
    /// <param name="request">The incoming provider request.</param>
    /// <returns>The normalized provider request.</returns>
    ProviderRequest Prepare(ProviderRequest request);
}
