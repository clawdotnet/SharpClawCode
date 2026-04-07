namespace SharpClaw.Code.Agents.Models;

/// <summary>
/// Describes a framework-backed agent invocation.
/// </summary>
/// <param name="AgentId">The stable agent identifier.</param>
/// <param name="AgentKind">The logical agent kind.</param>
/// <param name="Name">The human-readable agent name.</param>
/// <param name="Description">The agent description.</param>
/// <param name="Instructions">The system instructions used for the run.</param>
/// <param name="Context">The logical agent run context.</param>
public sealed record AgentFrameworkRequest(
    string AgentId,
    string AgentKind,
    string Name,
    string Description,
    string Instructions,
    AgentRunContext Context);
