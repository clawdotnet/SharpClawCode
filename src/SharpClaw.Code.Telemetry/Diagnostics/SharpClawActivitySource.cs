using System.Diagnostics;

namespace SharpClaw.Code.Telemetry.Diagnostics;

/// <summary>
/// Central <see cref="ActivitySource"/> for OpenTelemetry distributed tracing in SharpClaw Code.
/// Consumers wire this into their OpenTelemetry pipeline via <c>AddSource(SharpClawActivitySource.SourceName)</c>.
/// </summary>
public static class SharpClawActivitySource
{
    /// <summary>The ActivitySource name for OpenTelemetry configuration.</summary>
    public const string SourceName = "SharpClaw.Code";

    /// <summary>Shared ActivitySource instance.</summary>
    public static readonly ActivitySource Instance = new(SourceName, "1.0.0");
}
