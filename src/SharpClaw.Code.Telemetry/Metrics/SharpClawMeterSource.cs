using System.Diagnostics.Metrics;

namespace SharpClaw.Code.Telemetry.Metrics;

/// <summary>
/// Central Meter for SharpClaw Code runtime metrics.
/// Consumers wire this via <c>AddMeter(SharpClawMeterSource.MeterName)</c>.
/// </summary>
public static class SharpClawMeterSource
{
    /// <summary>
    /// Stable meter name used by OpenTelemetry configuration.
    /// </summary>
    public const string MeterName = "SharpClaw.Code";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Total input tokens consumed.</summary>
    public static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("sharpclaw.tokens.input", "tokens");

    /// <summary>Total output tokens consumed.</summary>
    public static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("sharpclaw.tokens.output", "tokens");

    /// <summary>Turn execution duration.</summary>
    public static readonly Histogram<double> TurnDuration = Meter.CreateHistogram<double>("sharpclaw.turn.duration", "ms");

    /// <summary>Provider request duration.</summary>
    public static readonly Histogram<double> ProviderDuration = Meter.CreateHistogram<double>("sharpclaw.provider.duration", "ms");

    /// <summary>Tool execution duration.</summary>
    public static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>("sharpclaw.tool.duration", "ms");

    /// <summary>Total tool invocations.</summary>
    public static readonly Counter<long> ToolInvocations = Meter.CreateCounter<long>("sharpclaw.tool.invocations", "invocations");
}
