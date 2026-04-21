using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Abstractions;

/// <summary>
/// Tracks the active runtime host context for the current async flow.
/// </summary>
public interface IRuntimeHostContextAccessor
{
    /// <summary>
    /// Gets the host context for the active async flow, when one is present.
    /// </summary>
    RuntimeHostContext? Current { get; }

    /// <summary>
    /// Begins a new host-context scope for the current async flow.
    /// </summary>
    IDisposable BeginScope(RuntimeHostContext? context);
}
