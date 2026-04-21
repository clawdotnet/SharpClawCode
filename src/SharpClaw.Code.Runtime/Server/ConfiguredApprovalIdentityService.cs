using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Server;

/// <summary>
/// Resolves approval principals from trusted headers or OIDC bearer tokens.
/// </summary>
public sealed class ConfiguredApprovalIdentityService(
    ISharpClawConfigService sharpClawConfigService) : IApprovalIdentityService
{
    private readonly ConcurrentDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> oidcConfigurationManagers = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<ApprovalAuthStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var options = await GetOptionsAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (options is null || options.Mode == ApprovalAuthMode.Disabled)
        {
            return new ApprovalAuthStatus(
                ApprovalAuthMode.Disabled,
                IsConfigured: false,
                IsHealthy: true,
                Detail: "Approval auth is disabled.");
        }

        if (options.Mode == ApprovalAuthMode.TrustedHeader)
        {
            var subjectHeader = options.SubjectHeader ?? "X-SharpClaw-User";
            return new ApprovalAuthStatus(
                options.Mode,
                IsConfigured: true,
                IsHealthy: !string.IsNullOrWhiteSpace(subjectHeader),
                RequireForAdmin: options.RequireForAdmin,
                RequireAuthenticatedApprovals: options.RequireAuthenticatedApprovals,
                Detail: $"Trusted-header approval auth is configured with subject header '{subjectHeader}'.");
        }

        if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.Audience))
        {
            return new ApprovalAuthStatus(
                options.Mode,
                IsConfigured: true,
                IsHealthy: false,
                RequireForAdmin: options.RequireForAdmin,
                RequireAuthenticatedApprovals: options.RequireAuthenticatedApprovals,
                Authority: options.Authority,
                Audience: options.Audience,
                Detail: "OIDC approval auth requires both authority and audience.");
        }

        try
        {
            _ = await GetConfigurationManager(options.Authority!).GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            return new ApprovalAuthStatus(
                options.Mode,
                IsConfigured: true,
                IsHealthy: true,
                RequireForAdmin: options.RequireForAdmin,
                RequireAuthenticatedApprovals: options.RequireAuthenticatedApprovals,
                Authority: options.Authority,
                Audience: options.Audience,
                Detail: "OIDC approval auth metadata resolved successfully.");
        }
        catch (Exception exception)
        {
            return new ApprovalAuthStatus(
                options.Mode,
                IsConfigured: true,
                IsHealthy: false,
                RequireForAdmin: options.RequireForAdmin,
                RequireAuthenticatedApprovals: options.RequireAuthenticatedApprovals,
                Authority: options.Authority,
                Audience: options.Audience,
                Detail: exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ApprovalPrincipal?> ResolveAsync(
        string workspaceRoot,
        ApprovalIdentityRequest request,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = await GetOptionsAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (options is null || options.Mode == ApprovalAuthMode.Disabled)
        {
            return null;
        }

        return options.Mode switch
        {
            ApprovalAuthMode.TrustedHeader => ResolveFromTrustedHeaders(request.Headers, options, hostContext),
            ApprovalAuthMode.Oidc => await ResolveFromOidcAsync(request.AuthorizationHeader, options, hostContext, cancellationToken).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<SharpClawApprovalAuthOptions?> GetOptionsAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var snapshot = await sharpClawConfigService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return snapshot.Document.Server?.ApprovalAuth;
    }

    private static ApprovalPrincipal? ResolveFromTrustedHeaders(
        IReadOnlyDictionary<string, string> headers,
        SharpClawApprovalAuthOptions options,
        RuntimeHostContext? hostContext)
    {
        var subjectHeader = options.SubjectHeader ?? "X-SharpClaw-User";
        if (!headers.TryGetValue(subjectHeader, out var subjectId) || string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        headers.TryGetValue(options.DisplayNameHeader ?? "X-SharpClaw-Display-Name", out var displayName);
        headers.TryGetValue(options.TenantHeader ?? "X-SharpClaw-Tenant-Id", out var tenantId);
        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? hostContext?.TenantId : tenantId;

        return new ApprovalPrincipal(
            SubjectId: subjectId.Trim(),
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            TenantId: string.IsNullOrWhiteSpace(effectiveTenantId) ? null : effectiveTenantId.Trim(),
            Roles: ReadDelimitedHeader(headers, options.RolesHeader ?? "X-SharpClaw-Roles"),
            Scopes: ReadDelimitedHeader(headers, options.ScopesHeader ?? "X-SharpClaw-Scopes"),
            AuthenticationType: "trusted-header");
    }

    private async Task<ApprovalPrincipal?> ResolveFromOidcAsync(
        string? authorizationHeader,
        SharpClawApprovalAuthOptions options,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.Audience))
        {
            return null;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var configuration = await GetConfigurationManager(options.Authority!).GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var validationParameters = new TokenValidationParameters
        {
            ValidAudience = options.Audience,
            ValidIssuers =
            [
                configuration.Issuer,
                options.Authority.TrimEnd('/'),
            ],
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, validationParameters).ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return null;
        }

        var subjectId = result.ClaimsIdentity.FindFirst(options.SubjectClaim ?? "sub")?.Value;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        var tenantId = result.ClaimsIdentity.FindFirst(options.TenantClaim ?? "tid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = hostContext?.TenantId;
        }

        return new ApprovalPrincipal(
            SubjectId: subjectId,
            DisplayName: result.ClaimsIdentity.FindFirst(options.DisplayNameClaim ?? "name")?.Value,
            TenantId: tenantId,
            Roles: ReadClaimValues(result.ClaimsIdentity, options.RolesClaim ?? "role"),
            Scopes: ReadScopeValues(result.ClaimsIdentity, options.ScopesClaim ?? "scope"),
            AuthenticationType: "oidc");
    }

    private IConfigurationManager<OpenIdConnectConfiguration> GetConfigurationManager(string authority)
        => oidcConfigurationManagers.GetOrAdd(
            authority.TrimEnd('/'),
            static key => new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{key}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever
                {
                    RequireHttps = false,
                }));

    private static string[]? ReadDelimitedHeader(IReadOnlyDictionary<string, string> headers, string headerName)
    {
        if (!headers.TryGetValue(headerName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var items = value
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return items.Length == 0 ? null : items;
    }

    private static string[]? ReadClaimValues(System.Security.Claims.ClaimsIdentity identity, string claimType)
    {
        var values = identity.FindAll(claimType)
            .Select(static claim => claim.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    private static string[]? ReadScopeValues(System.Security.Claims.ClaimsIdentity identity, string claimType)
    {
        var values = identity.FindAll(claimType)
            .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? null : values;
    }
}
