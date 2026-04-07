using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Abstractions;

/// <summary>
/// Streams model responses from a concrete provider implementation.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// Gets the provider name used for resolution.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the current authentication status for the provider.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider authentication status.</returns>
    Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts a streaming request for the specified provider payload.
    /// </summary>
    /// <param name="request">The normalized provider request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A streaming handle over provider events.</returns>
    Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken);
}
