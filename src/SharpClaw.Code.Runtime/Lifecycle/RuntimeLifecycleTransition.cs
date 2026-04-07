namespace SharpClaw.Code.Runtime.Lifecycle;

/// <summary>
/// Identifies explicit session lifecycle transitions.
/// </summary>
public enum RuntimeLifecycleTransition
{
    /// <summary>
    /// Activates a session for prompt execution.
    /// </summary>
    Activate,

    /// <summary>
    /// Marks a session as recovering.
    /// </summary>
    Recover,

    /// <summary>
    /// Marks a session as failed.
    /// </summary>
    Fail,

    /// <summary>
    /// Archives a session.
    /// </summary>
    Archive,
}
