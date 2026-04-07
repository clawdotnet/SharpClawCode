using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SharpClaw.Code.Agents.Internal;

internal sealed class SharpClawFrameworkAgent(
    string agentId,
    string name,
    string description,
    Func<IEnumerable<ChatMessage>, AgentSession, AgentRunOptions, CancellationToken, Task<AgentResponse>> runAsync) : AIAgent
{
    protected override string? IdCore => agentId;

    public override string? Name => name;

    public override string? Description => description;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult<AgentSession>(new SharpClawAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(JsonSerializer.SerializeToElement(session.StateBag, jsonSerializerOptions));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        var stateBag = serializedState.Deserialize<AgentSessionStateBag>(jsonSerializerOptions) ?? new AgentSessionStateBag();
        return ValueTask.FromResult<AgentSession>(new SharpClawAgentSession(stateBag));
    }

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
        => runAsync(messages, session ?? new SharpClawAgentSession(), options ?? new AgentRunOptions(), cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }
}
