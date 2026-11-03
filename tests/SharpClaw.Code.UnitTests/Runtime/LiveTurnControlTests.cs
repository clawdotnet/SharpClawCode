using FluentAssertions;
using SharpClaw.Code.Runtime.Turns;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class LiveTurnControlTests
{
    [Fact]
    public void Closing_steering_retains_pending_work_and_rejects_late_submissions()
    {
        using var handle = new LiveTurnControlService().Begin();
        handle.TrySteer("Keep this correction").Should().BeTrue();
        handle.CloseSteering().Should().Equal("Keep this correction");
        handle.TrySteer("Too late").Should().BeFalse();
        handle.GetPendingSteering().Should().Equal("Keep this correction");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_callbacks_can_access_the_handle_from_another_thread(bool dispose)
    {
        using var handle = new LiveTurnControlService().Begin();
        bool completed = false;
        using var registration = handle.CancellationToken.Register(() =>
            completed = Task.Run(() => handle.GetPendingSteering()).Wait(TimeSpan.FromSeconds(2)));
        if (dispose) handle.Dispose();
        else handle.Cancel();
        completed.Should().BeTrue("cancellation callbacks must run outside the handle lock");
    }
}
