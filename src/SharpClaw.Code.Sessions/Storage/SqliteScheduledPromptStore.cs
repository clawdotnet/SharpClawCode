using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores scheduled prompts in the workspace SQLite catalog.
/// </summary>
public sealed class SqliteScheduledPromptStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : IScheduledPromptStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledPromptDefinition>> ListAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM scheduled_prompts
            ORDER BY CASE WHEN next_run_utc IS NULL THEN 1 ELSE 0 END,
                     next_run_utc ASC,
                     updated_at_utc DESC;
            """;

        var items = new List<ScheduledPromptDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                var item = JsonSerializer.Deserialize(reader.GetString(0), ProtocolJsonContext.Default.ScheduledPromptDefinition);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<ScheduledPromptDefinition?> GetByIdAsync(string workspacePath, string scheduleId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM scheduled_prompts WHERE schedule_id = $scheduleId LIMIT 1;";
        command.Parameters.AddWithValue("$scheduleId", scheduleId);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.ScheduledPromptDefinition);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, ScheduledPromptDefinition definition, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO scheduled_prompts(schedule_id, updated_at_utc, enabled, next_run_utc, payload_json)
            VALUES ($scheduleId, $updatedAtUtc, $enabled, $nextRunUtc, $payloadJson)
            ON CONFLICT(schedule_id) DO UPDATE SET
                updated_at_utc = excluded.updated_at_utc,
                enabled = excluded.enabled,
                next_run_utc = excluded.next_run_utc,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$scheduleId", definition.Id);
        command.Parameters.AddWithValue("$updatedAtUtc", (definition.LastOutcome?.OccurredAtUtc ?? definition.LastRunUtc ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$enabled", definition.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$nextRunUtc", definition.NextRunUtc?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(definition, ProtocolJsonContext.Default.ScheduledPromptDefinition));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string workspacePath, string scheduleId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scheduled_prompts WHERE schedule_id = $scheduleId;";
        command.Parameters.AddWithValue("$scheduleId", scheduleId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }
}
