using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Lifecycle;

/// <summary>
/// Applies the initial explicit session lifecycle transitions.
/// </summary>
public sealed class DefaultRuntimeStateMachine : IRuntimeStateMachine
{
    /// <inheritdoc />
    public SessionLifecycleState Transition(SessionLifecycleState currentState, RuntimeLifecycleTransition transition)
        => transition switch
        {
            RuntimeLifecycleTransition.Activate when currentState is SessionLifecycleState.Created or SessionLifecycleState.Paused or SessionLifecycleState.Recovering or SessionLifecycleState.Active
                => SessionLifecycleState.Active,
            RuntimeLifecycleTransition.Recover when currentState is SessionLifecycleState.Active or SessionLifecycleState.Paused or SessionLifecycleState.Failed
                => SessionLifecycleState.Recovering,
            RuntimeLifecycleTransition.Fail when currentState is not SessionLifecycleState.Archived
                => SessionLifecycleState.Failed,
            RuntimeLifecycleTransition.Archive when currentState is not SessionLifecycleState.Archived
                => SessionLifecycleState.Archived,
            _ => throw new InvalidOperationException($"Cannot apply transition '{transition}' from state '{currentState}'.")
        };
}
