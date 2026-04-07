using FluentAssertions;
using SharpClaw.Code.Runtime;

namespace SharpClaw.Code.ParityHarness.Smoke;

/// <summary>
/// Confirms the parity harness can consume the runtime assembly.
/// </summary>
public sealed class RuntimeAssemblyMarkerTests
{
    /// <summary>
    /// Ensures the runtime marker type is available to the harness.
    /// </summary>
    [Fact]
    public void Runtime_marker_should_be_constructible()
    {
        var marker = new AssemblyMarker();

        marker.Should().NotBeNull();
    }
}
