using SharpClaw.Code.Runtime.Turns;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>Creates isolated live control scopes for CLI, editor, and embedded callers.</summary>
public interface ILiveTurnControlService
{
    /// <summary>Begins a scope in the caller's async flow. Pass its token to runtime execution and dispose after the execution settles.</summary>
    LiveTurnHandle Begin(CancellationToken cancellationToken = default);
}
