using FluentAssertions;
using SharpClaw.Code.Protocol.Contracts;

namespace SharpClaw.Code.UnitTests.Protocol;

/// <summary>
/// Covers the minimal protocol turn envelope contract.
/// </summary>
public sealed class TurnEnvelopeTests
{
    /// <summary>
    /// Ensures the record stores its constructor values.
    /// </summary>
    [Fact]
    public void Constructor_should_capture_session_and_input()
    {
        var envelope = new TurnEnvelope("session-001", "inspect workspace");

        envelope.SessionId.Should().Be("session-001");
        envelope.Input.Should().Be("inspect workspace");
    }
}
