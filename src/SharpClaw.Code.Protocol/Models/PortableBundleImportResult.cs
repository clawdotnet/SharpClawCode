namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Result of importing a <see cref="SessionBundleManifest"/>-backed zip into a workspace.
/// </summary>
/// <param name="SessionId">Imported session id (directory name under <c>sessions/</c>).</param>
/// <param name="BundleSchemaVersion">Schema from the bundle manifest.</param>
/// <param name="SourceWorkspaceHint">Optional hint from the manifest (non-authoritative).</param>
public sealed record PortableBundleImportResult(
    string SessionId,
    string BundleSchemaVersion,
    string? SourceWorkspaceHint);
