using FluentAssertions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Lifecycle;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class DefaultRuntimeStateMachineTests
{
    private readonly DefaultRuntimeStateMachine _machine = new();

    [Fact]
    public void Recover_from_Failed_then_Activate_reaches_Active()
    {
        var recovering = _machine.Transition(SessionLifecycleState.Failed, RuntimeLifecycleTransition.Recover);
        recovering.Should().Be(SessionLifecycleState.Recovering);

        var active = _machine.Transition(recovering, RuntimeLifecycleTransition.Activate);
        active.Should().Be(SessionLifecycleState.Active);
    }

    [Fact]
    public void Activate_from_Failed_without_recover_throws()
    {
        var act = () => _machine.Transition(SessionLifecycleState.Failed, RuntimeLifecycleTransition.Activate);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Fail_on_Archived_throws()
    {
        var act = () => _machine.Transition(SessionLifecycleState.Archived, RuntimeLifecycleTransition.Fail);
        act.Should().Throw<InvalidOperationException>();
    }
}
