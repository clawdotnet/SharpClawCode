using System.Text.Json;
using Microsoft.Data.Sqlite;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores core session snapshots in a SQLite catalog for embedded and hosted scenarios.
/// </summary>
public sealed class SqliteSessionStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : ISessionStore
{
    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions(session_id, updated_at_utc, payload_json)
            VALUES ($sessionId, $updatedAtUtc, $payloadJson)
            ON CONFLICT(session_id) DO UPDATE SET
                updated_at_utc = excluded.updated_at_utc,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$sessionId", session.Id);
        command.Parameters.AddWithValue("$updatedAtUtc", session.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(session, ProtocolJsonContext.Default.ConversationSession));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM sessions WHERE session_id = $sessionId LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return Deserialize(payload);
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM sessions
            ORDER BY updated_at_utc DESC
            LIMIT 1;
            """;
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return Deserialize(payload);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM sessions
            ORDER BY updated_at_utc DESC;
            """;

        var sessions = new List<ConversationSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = reader.IsDBNull(0) ? null : reader.GetString(0);
            var session = Deserialize(payload);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    private static ConversationSession? Deserialize(string? payload)
        => string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.ConversationSession);
}
