using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Lifecycle;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Applies explicit lifecycle transitions to runtime session state.
/// </summary>
public interface IRuntimeStateMachine
{
    /// <summary>
    /// Applies a lifecycle transition.
    /// </summary>
    /// <param name="currentState">The current session state.</param>
    /// <param name="transition">The transition to apply.</param>
    /// <returns>The resulting session state.</returns>
    SessionLifecycleState Transition(SessionLifecycleState currentState, RuntimeLifecycleTransition transition);
}
