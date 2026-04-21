using Microsoft.Data.Sqlite;
using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

internal static class SqliteSessionStoreDatabase
{
    public static async Task<SqliteConnection> OpenConnectionAsync(
        IFileSystem fileSystem,
        IRuntimeStoragePathResolver storagePathResolver,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var dbPath = storagePathResolver.GetSessionStoreDatabasePath(workspacePath);
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                updated_at_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_updated_at_utc ON sessions(updated_at_utc DESC);

            CREATE TABLE IF NOT EXISTS runtime_events (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                event_type TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_runtime_events_session_sequence ON runtime_events(session_id, sequence);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
