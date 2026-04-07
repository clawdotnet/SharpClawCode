namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents the current authentication status for a provider, plugin, or runtime principal.
/// </summary>
/// <param name="SubjectId">The authenticated subject identifier, if any.</param>
/// <param name="IsAuthenticated">Indicates whether authentication is currently valid.</param>
/// <param name="ProviderName">The authentication provider name, if any.</param>
/// <param name="OrganizationId">The related organization or tenant identifier, if any.</param>
/// <param name="ExpiresAtUtc">The UTC expiration timestamp, if known.</param>
/// <param name="GrantedScopes">The granted scopes or permissions associated with the status.</param>
public sealed record AuthStatus(
    string? SubjectId,
    bool IsAuthenticated,
    string? ProviderName,
    string? OrganizationId,
    DateTimeOffset? ExpiresAtUtc,
    string[]? GrantedScopes);
