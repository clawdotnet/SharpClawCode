using SharpClaw.Code.Agents.Models;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>Exposes control belonging only to the current caller's asynchronous turn flow.</summary>
public interface IAgentTurnControlAccessor
{
    /// <summary>Persists and consumes queued user steering at a complete tool boundary.</summary>
    Task<IReadOnlyList<string>> ConsumeSteeringAsync(AgentRunContext context, CancellationToken cancellationToken);
}
