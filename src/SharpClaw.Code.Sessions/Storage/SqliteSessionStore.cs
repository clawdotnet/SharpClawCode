using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores core session snapshots in a SQLite catalog for embedded and hosted scenarios.
/// </summary>
public sealed class SqliteSessionStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver,
    ILogger<SqliteSessionStore>? logger = null) : ISessionStore
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
        command.Parameters.AddWithValue("$payloadJson", SessionSnapshotSerializer.Serialize(session));
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
        return Deserialize(payload, sessionId);
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, payload_json
            FROM sessions
            ORDER BY updated_at_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sessionId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var payload = reader.IsDBNull(1) ? null : reader.GetString(1);
            var session = Deserialize(payload, sessionId);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, payload_json
            FROM sessions
            ORDER BY updated_at_utc DESC;
            """;

        var sessions = new List<ConversationSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sessionId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var payload = reader.IsDBNull(1) ? null : reader.GetString(1);
            var session = Deserialize(payload, sessionId);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    private ConversationSession? Deserialize(string? payload, string? sessionId)
    {
        try
        {
            return SessionSnapshotSerializer.Deserialize(payload);
        }
        catch (JsonException exception)
        {
            (logger ?? NullLogger<SqliteSessionStore>.Instance).LogWarning(
                exception,
                "Skipping unreadable SQLite session snapshot {SessionId}.",
                sessionId ?? "unknown");
            return null;
        }
    }
}
