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

    /// <summary>
    /// Lists configured hooks together with the most recent in-process test result, when any.
    /// </summary>
    Task<IReadOnlyList<HookStatusRecord>> ListAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Tests a single configured hook using the normal execution path.
    /// </summary>
    Task<HookTestResult> TestAsync(string workspaceRoot, string hookName, string payloadJson, CancellationToken cancellationToken);
}
