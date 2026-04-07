using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Telemetry.Export;

/// <summary>
/// Writes protocol-shaped JSON traces (runtime events and usage snapshots) for debugging or external tooling.
/// Hosts may persist output via the host file system or stream APIs.
/// </summary>
public sealed class JsonTraceExporter
{
    /// <summary>
    /// Serializes runtime events as a JSON array (polymorphic <see cref="RuntimeEvent" /> contracts).
    /// </summary>
    /// <param name="events">The events to export.</param>
    /// <param name="writeIndented">Whether to indent JSON.</param>
    /// <returns>UTF-8 JSON text.</returns>
    public string SerializeEvents(IReadOnlyList<RuntimeEvent> events, bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(events);
        return JsonSerializer.Serialize(events.ToArray(), CreateOptions(writeIndented));
    }

    /// <summary>
    /// Serializes cumulative usage keyed by session id.
    /// </summary>
    /// <param name="usageBySession">The usage map (e.g. from <see cref="Abstractions.IUsageTracker.GetCumulativeSnapshot" />()).</param>
    /// <param name="writeIndented">Whether to indent JSON.</param>
    /// <returns>UTF-8 JSON text.</returns>
    public string SerializeUsage(IReadOnlyDictionary<string, UsageSnapshot> usageBySession, bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(usageBySession);
        return JsonSerializer.Serialize(usageBySession, CreateOptions(writeIndented));
    }

    /// <summary>
    /// Writes serialized runtime events to a UTF-8 file (creates or overwrites).
    /// </summary>
    /// <param name="filePath">The destination path.</param>
    /// <param name="events">The events to export.</param>
    /// <param name="writeIndented">Whether to indent JSON.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task WriteEventsFileAsync(
        string filePath,
        IReadOnlyList<RuntimeEvent> events,
        bool writeIndented = true,
        CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(filePath, SerializeEvents(events, writeIndented), cancellationToken);

    /// <summary>
    /// Writes cumulative usage JSON to a UTF-8 file (creates or overwrites).
    /// </summary>
    /// <param name="filePath">The destination path.</param>
    /// <param name="usageBySession">The usage map.</param>
    /// <param name="writeIndented">Whether to indent JSON.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task WriteUsageFileAsync(
        string filePath,
        IReadOnlyDictionary<string, UsageSnapshot> usageBySession,
        bool writeIndented = true,
        CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(filePath, SerializeUsage(usageBySession, writeIndented), cancellationToken);

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
        => new()
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = ProtocolJsonContext.Default
        };
}
