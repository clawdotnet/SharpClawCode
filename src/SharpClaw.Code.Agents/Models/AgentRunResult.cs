using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Agents.Models;

/// <summary>
/// Represents the outcome of a logical agent run.
/// </summary>
/// <param name="AgentId">The agent identifier.</param>
/// <param name="AgentKind">The logical agent kind.</param>
/// <param name="Output">The final textual output.</param>
/// <param name="Usage">The usage snapshot for the run.</param>
/// <param name="Summary">A concise execution summary.</param>
/// <param name="ProviderRequest">The provider request used during the run, if any.</param>
/// <param name="ProviderEvents">The provider events emitted during the run, if any.</param>
/// <param name="ToolResults">The tool results produced during the run, if any.</param>
/// <param name="Events">The runtime events produced during the run.</param>
public sealed record AgentRunResult(
    string AgentId,
    string AgentKind,
    string Output,
    UsageSnapshot Usage,
    string Summary,
    ProviderRequest? ProviderRequest = null,
    IReadOnlyList<ProviderEvent>? ProviderEvents = null,
    IReadOnlyList<ToolResult>? ToolResults = null,
    IReadOnlyList<RuntimeEvent>? Events = null);
