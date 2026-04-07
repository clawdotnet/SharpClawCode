using Microsoft.Agents.AI;

namespace SharpClaw.Code.Agents.Internal;

internal sealed class SharpClawAgentSession : AgentSession
{
    public SharpClawAgentSession()
    {
    }

    public SharpClawAgentSession(AgentSessionStateBag stateBag)
        : base(stateBag)
    {
    }
}
