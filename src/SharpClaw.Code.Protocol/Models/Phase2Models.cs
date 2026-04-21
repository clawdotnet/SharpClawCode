using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Selects the durable session/event storage backend for an embedded host.
/// </summary>
public enum SessionStoreKind
{
    /// <summary>
    /// Persist session artifacts as files under workspace or configured storage roots.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Persist core session artifacts in SQLite.
    /// </summary>
    Sqlite,
}

/// <summary>
/// Describes the current embedded host and tenant boundary for a runtime invocation.
/// </summary>
/// <param name="HostId">Stable host identifier for emitted events and diagnostics.</param>
/// <param name="TenantId">Optional tenant identifier.</param>
/// <param name="StorageRoot">Optional external storage root for durable state.</param>
/// <param name="SessionStoreKind">Selected session store backend.</param>
/// <param name="IsEmbeddedHost">Whether the runtime is being used as an embedded SDK host.</param>
public sealed record RuntimeHostContext(
    string HostId,
    string? TenantId = null,
    string? StorageRoot = null,
    SessionStoreKind SessionStoreKind = SessionStoreKind.FileSystem,
    bool IsEmbeddedHost = false);

/// <summary>
/// Wraps a runtime event with host and routing metadata for external streaming.
/// </summary>
/// <param name="EventType">Stable event type name.</param>
/// <param name="OccurredAtUtc">Event timestamp.</param>
/// <param name="Event">The underlying runtime event payload.</param>
/// <param name="WorkspacePath">Normalized workspace path when known.</param>
/// <param name="SessionId">Session identifier when known.</param>
/// <param name="TenantId">Tenant identifier when present.</param>
/// <param name="HostId">Embedded host identifier when present.</param>
public sealed record RuntimeEventEnvelope(
    string EventType,
    DateTimeOffset OccurredAtUtc,
    RuntimeEvent Event,
    string? WorkspacePath = null,
    string? SessionId = null,
    string? TenantId = null,
    string? HostId = null);

/// <summary>
/// Describes the packaged distribution metadata for a custom tool bundle.
/// </summary>
/// <param name="PackageId">Stable package identifier.</param>
/// <param name="Version">Package version.</param>
/// <param name="PackageType">Distribution kind, such as <c>nuget</c> or <c>local</c>.</param>
/// <param name="EntryAssembly">Entry assembly or process path.</param>
/// <param name="EntryArguments">Optional arguments passed to the entry assembly or process.</param>
/// <param name="TargetFramework">Target framework moniker.</param>
/// <param name="Tags">Optional discovery tags.</param>
public sealed record ToolPackageReference(
    string PackageId,
    string Version,
    string PackageType,
    string EntryAssembly,
    string[]? EntryArguments = null,
    string? TargetFramework = null,
    string[]? Tags = null);

/// <summary>
/// Declares one tool surfaced from a packaged tool bundle.
/// </summary>
/// <param name="Name">Stable tool name.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="InputSchemaJson">Optional JSON schema or example payload.</param>
/// <param name="RequiresApproval">Whether the tool requires approval by default.</param>
/// <param name="IsDestructive">Whether the tool mutates workspace or environment state.</param>
/// <param name="Tags">Optional discovery tags.</param>
public sealed record PackagedToolDescriptor(
    string Name,
    string Description,
    string? InputSchemaJson,
    bool RequiresApproval = false,
    bool IsDestructive = false,
    string[]? Tags = null);

/// <summary>
/// Manifest describing a distributable custom tool package.
/// </summary>
/// <param name="Package">Package distribution metadata.</param>
/// <param name="PublisherId">Optional publisher identifier.</param>
/// <param name="Description">Optional package description.</param>
/// <param name="Tools">Tools provided by the package.</param>
public sealed record ToolPackageManifest(
    ToolPackageReference Package,
    string? PublisherId,
    string? Description,
    PackagedToolDescriptor[] Tools);

/// <summary>
/// Represents one installed tool package in the local workspace catalog.
/// </summary>
/// <param name="Manifest">The installed manifest.</param>
/// <param name="InstalledAtUtc">Installation time.</param>
/// <param name="InstallSource">Source used to install the package.</param>
/// <param name="ResolvedInstall">Resolved install metadata retained after activation.</param>
public sealed record InstalledToolPackage(
    ToolPackageManifest Manifest,
    DateTimeOffset InstalledAtUtc,
    string InstallSource,
    ToolPackageResolvedInstall? ResolvedInstall = null);

/// <summary>
/// Request payload used to install a packaged tool manifest into a workspace catalog.
/// </summary>
/// <param name="Manifest">The manifest to install.</param>
/// <param name="InstallSource">The source identifier or path for the install action.</param>
/// <param name="EnableAfterInstall">Whether the underlying plugin should be enabled immediately.</param>
/// <param name="SourceReference">Optional local directory, binary path, or package file reference.</param>
/// <param name="PackageSource">Optional package feed or source URL.</param>
public sealed record ToolPackageInstallRequest(
    ToolPackageManifest Manifest,
    string InstallSource,
    bool EnableAfterInstall = true,
    string? SourceReference = null,
    string? PackageSource = null);
