using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Abstractions;

/// <summary>
/// Exposes provider authentication status checks.
/// </summary>
public interface IAuthFlowService
{
    /// <summary>
    /// Gets the authentication status for a provider.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authentication status.</returns>
    Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken);
}
