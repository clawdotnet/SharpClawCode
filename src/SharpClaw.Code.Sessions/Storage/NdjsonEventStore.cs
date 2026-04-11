using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores session runtime events in append-only NDJSON files.
/// </summary>
public sealed class NdjsonEventStore(IFileSystem fileSystem, IPathService pathService, ILogger<NdjsonEventStore>? logger = null) : IEventStore
{
    /// <inheritdoc />
    public Task AppendAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(runtimeEvent, ProtocolJsonContext.Default.RuntimeEvent);
        var path = SessionStorageLayout.GetEventsPath(pathService, workspacePath, sessionId);
        return fileSystem.AppendLineAsync(path, json, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        var path = SessionStorageLayout.GetEventsPath(pathService, workspacePath, sessionId);
        var lines = await fileSystem.ReadAllLinesIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        var events = new List<RuntimeEvent>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var runtimeEvent = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.RuntimeEvent);
                if (runtimeEvent is not null)
                {
                    events.Add(runtimeEvent);
                }
            }
            catch (JsonException ex)
            {
                (logger ?? NullLogger<NdjsonEventStore>.Instance).LogWarning(ex, "Skipping malformed NDJSON event line in session {SessionId}.", sessionId);
            }
        }

        return events;
    }
}
