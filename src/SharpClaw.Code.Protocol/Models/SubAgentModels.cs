namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Single task request for the synthetic subagent delegation tool.
/// </summary>
/// <param name="Goal">The bounded investigation goal.</param>
/// <param name="ExpectedOutput">The expected output shape.</param>
/// <param name="Constraints">Optional task constraints.</param>
public sealed record SubAgentTaskRequest(
    string Goal,
    string ExpectedOutput,
    string[]? Constraints);

/// <summary>
/// Batch request for the synthetic subagent delegation tool.
/// </summary>
/// <param name="Tasks">The tasks to delegate.</param>
public sealed record SubAgentBatchRequest(
    SubAgentTaskRequest[] Tasks);

/// <summary>
/// Result for a single delegated subagent task.
/// </summary>
/// <param name="TaskId">The generated delegated task id.</param>
/// <param name="Goal">The original task goal.</param>
/// <param name="ExpectedOutput">The requested output contract.</param>
/// <param name="Succeeded">Whether the subagent completed successfully.</param>
/// <param name="Output">The subagent output when successful.</param>
/// <param name="ErrorMessage">The failure message when unsuccessful.</param>
/// <param name="AgentId">The agent id that executed the task.</param>
public sealed record SubAgentTaskResult(
    string TaskId,
    string Goal,
    string ExpectedOutput,
    bool Succeeded,
    string? Output,
    string? ErrorMessage,
    string AgentId);

/// <summary>
/// Batch result for the synthetic subagent delegation tool.
/// </summary>
/// <param name="Tasks">The individual task outcomes.</param>
/// <param name="CompletedCount">The number of successful task completions.</param>
/// <param name="FailedCount">The number of failed task completions.</param>
public sealed record SubAgentBatchResult(
    SubAgentTaskResult[] Tasks,
    int CompletedCount,
    int FailedCount);
