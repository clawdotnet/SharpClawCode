using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Dispatches configured runtime hooks.
/// </summary>
public interface IHookDispatcher
{
    /// <summary>
    /// Dispatches hooks for the supplied trigger using a compact JSON payload.
    /// </summary>
    Task DispatchAsync(string workspaceRoot, HookTriggerKind trigger, string payloadJson, CancellationToken cancellationToken);
}
