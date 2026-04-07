using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

internal static class ProviderAuthStatusFactory
{
    public static AuthStatus FromApiKeyPresence(string providerName, string? apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        var ok = !string.IsNullOrWhiteSpace(apiKey);
        return new AuthStatus(
            SubjectId: null,
            IsAuthenticated: ok,
            ProviderName: providerName,
            OrganizationId: null,
            ExpiresAtUtc: null,
            GrantedScopes: ok ? ["api"] : []);
    }
}
