using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Agents.Models;

/// <summary>
/// Carries the batch result and runtime events emitted by delegated subagent execution.
/// </summary>
/// <param name="Result">The batch execution payload.</param>
/// <param name="Events">Runtime events emitted by delegated agent runs.</param>
public sealed record SubAgentBatchExecutionResult(
    SubAgentBatchResult Result,
    IReadOnlyList<RuntimeEvent> Events);
