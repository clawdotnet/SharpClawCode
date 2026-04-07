using System.Text.Json.Serialization;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Represents a base runtime event emitted during SharpClaw execution.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The parent session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$eventType")]
[JsonDerivedType(typeof(SessionCreatedEvent), "sessionCreated")]
[JsonDerivedType(typeof(SessionForkedEvent), "sessionForked")]
[JsonDerivedType(typeof(SessionStateChangedEvent), "sessionStateChanged")]
[JsonDerivedType(typeof(TurnStartedEvent), "turnStarted")]
[JsonDerivedType(typeof(TurnCompletedEvent), "turnCompleted")]
[JsonDerivedType(typeof(ToolStartedEvent), "toolStarted")]
[JsonDerivedType(typeof(ToolCompletedEvent), "toolCompleted")]
[JsonDerivedType(typeof(PermissionRequestedEvent), "permissionRequested")]
[JsonDerivedType(typeof(PermissionResolvedEvent), "permissionResolved")]
[JsonDerivedType(typeof(AgentSpawnedEvent), "agentSpawned")]
[JsonDerivedType(typeof(AgentCompletedEvent), "agentCompleted")]
[JsonDerivedType(typeof(McpStateChangedEvent), "mcpStateChanged")]
[JsonDerivedType(typeof(PluginStateChangedEvent), "pluginStateChanged")]
[JsonDerivedType(typeof(PluginInstalledEvent), "pluginInstalled")]
[JsonDerivedType(typeof(PluginUninstalledEvent), "pluginUninstalled")]
[JsonDerivedType(typeof(PluginUpdatedEvent), "pluginUpdated")]
[JsonDerivedType(typeof(ProviderStartedEvent), "providerStarted")]
[JsonDerivedType(typeof(ProviderDeltaEvent), "providerDelta")]
[JsonDerivedType(typeof(ProviderCompletedEvent), "providerCompleted")]
[JsonDerivedType(typeof(UsageUpdatedEvent), "usageUpdated")]
[JsonDerivedType(typeof(RecoveryAttemptedEvent), "recoveryAttempted")]
[JsonDerivedType(typeof(MutationSetRecordedEvent), "mutationSetRecorded")]
[JsonDerivedType(typeof(UndoRequestedEvent), "undoRequested")]
[JsonDerivedType(typeof(UndoCompletedEvent), "undoCompleted")]
[JsonDerivedType(typeof(UndoFailedEvent), "undoFailed")]
[JsonDerivedType(typeof(RedoRequestedEvent), "redoRequested")]
[JsonDerivedType(typeof(RedoCompletedEvent), "redoCompleted")]
[JsonDerivedType(typeof(RedoFailedEvent), "redoFailed")]
public abstract record RuntimeEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc);
