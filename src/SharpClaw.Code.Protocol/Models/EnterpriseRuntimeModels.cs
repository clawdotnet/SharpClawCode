using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Configures approval-identity handling for the embedded HTTP and admin surfaces.
/// </summary>
public enum ApprovalAuthMode
{
    /// <summary>
    /// Approval identity is disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// Approval identity is resolved from trusted upstream headers.
    /// </summary>
    TrustedHeader,

    /// <summary>
    /// Approval identity is resolved from an OIDC bearer token.
    /// </summary>
    Oidc,
}

/// <summary>
/// Describes approval-auth configuration resolved for the embedded server.
/// </summary>
public sealed record SharpClawApprovalAuthOptions(
    ApprovalAuthMode Mode = ApprovalAuthMode.Disabled,
    bool RequireForAdmin = false,
    bool RequireAuthenticatedApprovals = false,
    string? Authority = null,
    string? Audience = null,
    string? SubjectHeader = null,
    string? DisplayNameHeader = null,
    string? TenantHeader = null,
    string? RolesHeader = null,
    string? ScopesHeader = null,
    string? SubjectClaim = null,
    string? DisplayNameClaim = null,
    string? TenantClaim = null,
    string? RolesClaim = null,
    string? ScopesClaim = null);

/// <summary>
/// Represents the current approver identity for an authenticated approval flow.
/// </summary>
public sealed record ApprovalPrincipal(
    string SubjectId,
    string? DisplayName = null,
    string? TenantId = null,
    string[]? Roles = null,
    string[]? Scopes = null,
    string? AuthenticationType = null);

/// <summary>
/// Describes the configured approval-auth mode and current health.
/// </summary>
public sealed record ApprovalAuthStatus(
    ApprovalAuthMode Mode,
    bool IsConfigured,
    bool IsHealthy,
    bool RequireForAdmin = false,
    bool RequireAuthenticatedApprovals = false,
    string? Authority = null,
    string? Audience = null,
    string? Detail = null);

/// <summary>
/// Carries request headers used to resolve an approval principal.
/// </summary>
public sealed record ApprovalIdentityRequest(
    string? AuthorizationHeader,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Requests creation of a durable session through the admin API.
/// </summary>
public sealed record AdminCreateSessionRequest(
    PermissionMode? PermissionMode = null,
    OutputFormat? OutputFormat = null);

/// <summary>
/// Requests a metering summary or detail report.
/// </summary>
public sealed record UsageMeteringQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? TenantId = null,
    string? HostId = null,
    string? WorkspaceRoot = null,
    string? SessionId = null);

/// <summary>
/// Declares the type of persisted usage metering record.
/// </summary>
public enum UsageMeteringRecordKind
{
    /// <summary>
    /// A provider request completed.
    /// </summary>
    ProviderUsage,

    /// <summary>
    /// A workspace or session usage snapshot was updated.
    /// </summary>
    UsageSnapshot,

    /// <summary>
    /// A tool execution completed.
    /// </summary>
    ToolExecution,

    /// <summary>
    /// A turn completed.
    /// </summary>
    TurnExecution,

    /// <summary>
    /// A session lifecycle event occurred.
    /// </summary>
    SessionLifecycle,
}

/// <summary>
/// One normalized usage metering record.
/// </summary>
public sealed record UsageMeteringRecord(
    string Id,
    UsageMeteringRecordKind Kind,
    DateTimeOffset OccurredAtUtc,
    string? TenantId,
    string? HostId,
    string? WorkspaceRoot,
    string? SessionId,
    string? TurnId,
    string? ProviderName = null,
    string? Model = null,
    string? ToolName = null,
    ApprovalScope? ApprovalScope = null,
    bool? Succeeded = null,
    long? DurationMilliseconds = null,
    UsageSnapshot? Usage = null,
    string? Detail = null);

/// <summary>
/// Aggregated metering totals for a filtered query.
/// </summary>
public sealed record UsageMeteringSummaryReport(
    UsageMeteringQuery Query,
    UsageSnapshot TotalUsage,
    int ProviderRequestCount,
    int ToolExecutionCount,
    int TurnCount,
    int SessionEventCount);

/// <summary>
/// Detailed metering records for a filtered query.
/// </summary>
public sealed record UsageMeteringDetailReport(
    UsageMeteringQuery Query,
    IReadOnlyList<UsageMeteringRecord> Records);

/// <summary>
/// Resolved installation metadata retained for an installed packaged tool.
/// </summary>
public sealed record ToolPackageResolvedInstall(
    string? SourceReference,
    string? PackageSource,
    string? PackageFilePath,
    string? ExtractedPackageRoot,
    string ResolvedEntryAssembly,
    string[]? ResolvedEntryArguments = null);
