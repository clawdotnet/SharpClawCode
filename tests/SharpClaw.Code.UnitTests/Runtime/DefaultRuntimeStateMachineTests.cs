using FluentAssertions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Lifecycle;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class DefaultRuntimeStateMachineTests
{
    private readonly DefaultRuntimeStateMachine _machine = new();

    // ── Activate transition ──

    [Theory]
    [InlineData(SessionLifecycleState.Created)]
    [InlineData(SessionLifecycleState.Paused)]
    [InlineData(SessionLifecycleState.Recovering)]
    [InlineData(SessionLifecycleState.Active)]
    public void Activate_succeeds_from_valid_states(SessionLifecycleState from)
    {
        _machine.Transition(from, RuntimeLifecycleTransition.Activate)
            .Should().Be(SessionLifecycleState.Active);
    }

    [Theory]
    [InlineData(SessionLifecycleState.Failed)]
    [InlineData(SessionLifecycleState.Archived)]
    public void Activate_throws_from_invalid_states(SessionLifecycleState from)
    {
        var act = () => _machine.Transition(from, RuntimeLifecycleTransition.Activate);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Recover transition ──

    [Theory]
    [InlineData(SessionLifecycleState.Active)]
    [InlineData(SessionLifecycleState.Paused)]
    [InlineData(SessionLifecycleState.Failed)]
    [InlineData(SessionLifecycleState.Recovering)]
    public void Recover_succeeds_from_valid_states(SessionLifecycleState from)
    {
        _machine.Transition(from, RuntimeLifecycleTransition.Recover)
            .Should().Be(SessionLifecycleState.Recovering);
    }

    [Theory]
    [InlineData(SessionLifecycleState.Created)]
    [InlineData(SessionLifecycleState.Archived)]
    public void Recover_throws_from_invalid_states(SessionLifecycleState from)
    {
        var act = () => _machine.Transition(from, RuntimeLifecycleTransition.Recover);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Fail transition ──

    [Theory]
    [InlineData(SessionLifecycleState.Created)]
    [InlineData(SessionLifecycleState.Active)]
    [InlineData(SessionLifecycleState.Paused)]
    [InlineData(SessionLifecycleState.Recovering)]
    [InlineData(SessionLifecycleState.Failed)]
    public void Fail_succeeds_from_non_archived_states(SessionLifecycleState from)
    {
        _machine.Transition(from, RuntimeLifecycleTransition.Fail)
            .Should().Be(SessionLifecycleState.Failed);
    }

    [Fact]
    public void Fail_throws_from_Archived()
    {
        var act = () => _machine.Transition(SessionLifecycleState.Archived, RuntimeLifecycleTransition.Fail);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Archive transition ──

    [Theory]
    [InlineData(SessionLifecycleState.Created)]
    [InlineData(SessionLifecycleState.Active)]
    [InlineData(SessionLifecycleState.Paused)]
    [InlineData(SessionLifecycleState.Recovering)]
    [InlineData(SessionLifecycleState.Failed)]
    public void Archive_succeeds_from_non_archived_states(SessionLifecycleState from)
    {
        _machine.Transition(from, RuntimeLifecycleTransition.Archive)
            .Should().Be(SessionLifecycleState.Archived);
    }

    [Fact]
    public void Archive_throws_from_Archived()
    {
        var act = () => _machine.Transition(SessionLifecycleState.Archived, RuntimeLifecycleTransition.Archive);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Multi-step flows ──

    [Fact]
    public void Recover_from_Failed_then_Activate_reaches_Active()
    {
        var recovering = _machine.Transition(SessionLifecycleState.Failed, RuntimeLifecycleTransition.Recover);
        recovering.Should().Be(SessionLifecycleState.Recovering);

        var active = _machine.Transition(recovering, RuntimeLifecycleTransition.Activate);
        active.Should().Be(SessionLifecycleState.Active);
    }

    [Fact]
    public void Full_lifecycle_Created_to_Archived()
    {
        var state = SessionLifecycleState.Created;
        state = _machine.Transition(state, RuntimeLifecycleTransition.Activate);
        state.Should().Be(SessionLifecycleState.Active);

        state = _machine.Transition(state, RuntimeLifecycleTransition.Fail);
        state.Should().Be(SessionLifecycleState.Failed);

        state = _machine.Transition(state, RuntimeLifecycleTransition.Recover);
        state.Should().Be(SessionLifecycleState.Recovering);

        state = _machine.Transition(state, RuntimeLifecycleTransition.Activate);
        state.Should().Be(SessionLifecycleState.Active);

        state = _machine.Transition(state, RuntimeLifecycleTransition.Archive);
        state.Should().Be(SessionLifecycleState.Archived);
    }

    [Fact]
    public void Recover_is_idempotent_from_Recovering()
    {
        _machine.Transition(SessionLifecycleState.Recovering, RuntimeLifecycleTransition.Recover)
            .Should().Be(SessionLifecycleState.Recovering);
    }

    [Fact]
    public void Activate_is_idempotent_from_Active()
    {
        _machine.Transition(SessionLifecycleState.Active, RuntimeLifecycleTransition.Activate)
            .Should().Be(SessionLifecycleState.Active);
    }
}
