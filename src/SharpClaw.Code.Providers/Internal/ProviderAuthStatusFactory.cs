using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

internal static class ProviderAuthStatusFactory
{
    public static AuthStatus FromConfiguration(
        string providerName,
        string? apiKey,
        ProviderAuthMode authMode,
        bool hasAuthOptionalRuntime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        var ok = authMode switch
        {
            ProviderAuthMode.None => true,
            ProviderAuthMode.Optional => true,
            _ => !string.IsNullOrWhiteSpace(apiKey) || hasAuthOptionalRuntime
        };
        return new AuthStatus(
            SubjectId: null,
            IsAuthenticated: ok,
            ProviderName: providerName,
            OrganizationId: null,
            ExpiresAtUtc: null,
            GrantedScopes: ok ? ["api"] : []);
    }
}
