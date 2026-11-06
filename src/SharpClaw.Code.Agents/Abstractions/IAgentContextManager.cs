using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>Maintains bounded provider context at complete tool-loop boundaries.</summary>
public interface IAgentContextManager
{
    /// <summary>Persists any compaction before returning replacement messages. Never executes tools.</summary>
    Task<AgentContextPreparation> PrepareAsync(AgentRunContext context, IModelProvider provider, ProviderRequest request, CancellationToken cancellationToken);
}

/// <summary>A prepared request and the measured usage of any summarization call.</summary>
public sealed record AgentContextPreparation(ProviderRequest Request, UsageSnapshot? CompactionUsage = null);
