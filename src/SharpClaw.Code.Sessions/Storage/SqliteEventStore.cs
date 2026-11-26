using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores append-only runtime events in the shared SQLite session catalog.
/// </summary>
public sealed class SqliteEventStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : IEventStore
{
    /// <inheritdoc />
    public async Task AppendAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_events(session_id, occurred_at_utc, event_type, payload_json)
            VALUES ($sessionId, $occurredAtUtc, $eventType, $payloadJson);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$occurredAtUtc", runtimeEvent.OccurredAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$eventType", runtimeEvent.GetType().Name);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(runtimeEvent, ProtocolJsonContext.Default.RuntimeEvent));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM runtime_events
            WHERE session_id = $sessionId
            ORDER BY sequence ASC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var events = new List<RuntimeEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var runtimeEvent = JsonSerializer.Deserialize(reader.GetString(0), ProtocolJsonContext.Default.RuntimeEvent);
            if (runtimeEvent is not null)
            {
                events.Add(runtimeEvent);
            }
        }

        return events;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeEvent>> ReadLatestAsync(string workspacePath, string sessionId, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM runtime_events
            WHERE session_id = $sessionId
            ORDER BY sequence DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$count", count);

        var events = new List<RuntimeEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var runtimeEvent = JsonSerializer.Deserialize(reader.GetString(0), ProtocolJsonContext.Default.RuntimeEvent);
            if (runtimeEvent is not null)
            {
                events.Add(runtimeEvent);
            }
        }

        events.Reverse();
        return events;
    }
}
