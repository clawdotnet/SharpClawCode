using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Turns;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>An aggregate agent outcome with all attempts' accounting and already-persisted loop events.</summary>
public sealed record VerificationLoopResult(AgentRunResult AgentResult, VerificationRunReport? Verification, ProviderInvocationRecord[] ProviderInvocations, RuntimeEvent[] PersistedEvents);
/// <summary>Coordinates permission-aware verification and bounded agent repair without recursive turns.</summary>
public interface IVerificationLoopCoordinator
{
    /// <summary>Reuses the same agent, caller context and ordered mutation recorder across attempts.</summary>
    Task<VerificationLoopResult> RunAsync(ISharpClawAgent agent, AgentRunContext context, AgentRunResult initial, TurnMutationAccumulator mutations, VerificationPolicy policy, CancellationToken cancellationToken);
}
