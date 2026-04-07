using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Exposes provider authentication status through the registered provider clients.
/// </summary>
public sealed class AuthFlowService(IModelProviderResolver providerResolver) : IAuthFlowService
{
    /// <inheritdoc />
    public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
        => providerResolver.Resolve(providerName).GetAuthStatusAsync(cancellationToken);
}
