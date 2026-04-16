using System.Globalization;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Persists workspace knowledge and durable memory in SQLite databases under SharpClaw storage roots.
/// </summary>
public sealed class SqliteWorkspaceKnowledgeStore(
    IFileSystem fileSystem,
    IPathService pathService,
    IUserProfilePaths userProfilePaths) : IWorkspaceKnowledgeStore
{
    private const string WorkspaceKnowledgeDirectoryName = "knowledge";
    private const string WorkspaceKnowledgeFileName = "knowledge.db";
    private const string UserMemoryFileName = "user-memory.db";

    /// <inheritdoc />
    public async Task ReplaceWorkspaceIndexAsync(
        string workspaceRoot,
        WorkspaceIndexDocument document,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWorkspaceConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM indexed_chunks;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM indexed_chunks_fts;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM indexed_symbols;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM project_edges;", cancellationToken).ConfigureAwait(false);

        foreach (var chunk in document.Chunks)
        {
            await InsertChunkAsync(connection, transaction, chunk, cancellationToken).ConfigureAwait(false);
        }

        foreach (var symbol in document.Symbols)
        {
            await InsertSymbolAsync(connection, transaction, symbol, cancellationToken).ConfigureAwait(false);
        }

        foreach (var edge in document.ProjectEdges)
        {
            await InsertEdgeAsync(connection, transaction, edge, cancellationToken).ConfigureAwait(false);
        }

        await UpsertMetadataAsync(connection, transaction, "workspace_root", pathService.GetFullPath(workspaceRoot), cancellationToken).ConfigureAwait(false);
        await UpsertMetadataAsync(connection, transaction, "refreshed_at_utc", refreshedAtUtc.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkspaceIndexStatus> GetWorkspaceIndexStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await using var connection = await OpenWorkspaceConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var refreshedAt = await TryReadMetadataDateAsync(connection, "refreshed_at_utc", cancellationToken).ConfigureAwait(false);
        return new WorkspaceIndexStatus(
            WorkspaceRoot: pathService.GetFullPath(workspaceRoot),
            RefreshedAtUtc: refreshedAt,
            IndexedFileCount: await CountDistinctAsync(connection, "indexed_chunks", "path", cancellationToken).ConfigureAwait(false),
            ChunkCount: await CountAsync(connection, "indexed_chunks", cancellationToken).ConfigureAwait(false),
            SymbolCount: await CountAsync(connection, "indexed_symbols", cancellationToken).ConfigureAwait(false),
            ProjectEdgeCount: await CountAsync(connection, "project_edges", cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceSearchHit>> SearchChunksLexicalAsync(
        string workspaceRoot,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeFtsQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        await using var connection = await OpenWorkspaceConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var results = new List<WorkspaceSearchHit>();
        var sql = """
            SELECT c.path, c.excerpt, c.start_line, c.end_line, ABS(bm25(indexed_chunks_fts)) AS score
            FROM indexed_chunks_fts f
            JOIN indexed_chunks c ON c.id = f.id
            WHERE indexed_chunks_fts MATCH $query
            ORDER BY bm25(indexed_chunks_fts)
            LIMIT $limit;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new WorkspaceSearchHit(
                Path: reader.GetString(0),
                Kind: WorkspaceSearchHitKind.Lexical,
                Score: 1d / (1d + reader.GetDouble(4)),
                Excerpt: reader.GetString(1),
                SymbolName: null,
                SymbolKind: null,
                StartLine: reader.GetInt32(2),
                EndLine: reader.GetInt32(3)));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceSearchHit>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWorkspaceConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var results = new List<WorkspaceSearchHit>();
        var sql = """
            SELECT path, name, kind, container, line, column_number
            FROM indexed_symbols
            WHERE name LIKE $pattern OR COALESCE(container, '') LIKE $pattern
            ORDER BY CASE WHEN name = $exact THEN 0 WHEN name LIKE $prefix THEN 1 ELSE 2 END, name
            LIMIT $limit;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$pattern", $"%{query}%");
        command.Parameters.AddWithValue("$exact", query);
        command.Parameters.AddWithValue("$prefix", $"{query}%");
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(1);
            var container = reader.IsDBNull(3) ? null : reader.GetString(3);
            results.Add(new WorkspaceSearchHit(
                Path: reader.GetString(0),
                Kind: WorkspaceSearchHitKind.Symbol,
                Score: ScoreSymbolHit(query, name, container),
                Excerpt: container is null ? name : $"{container}.{name}",
                SymbolName: name,
                SymbolKind: reader.GetString(2),
                StartLine: reader.GetInt32(4),
                EndLine: reader.GetInt32(4)));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedWorkspaceChunk>> ListChunksAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await using var connection = await OpenWorkspaceConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var sql = """
            SELECT id, path, language, excerpt, content, start_line, end_line, embedding
            FROM indexed_chunks;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var results = new List<IndexedWorkspaceChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new IndexedWorkspaceChunk(
                Id: reader.GetString(0),
                Path: reader.GetString(1),
                Language: reader.GetString(2),
                Excerpt: reader.GetString(3),
                Content: reader.GetString(4),
                StartLine: reader.GetInt32(5),
                EndLine: reader.GetInt32(6),
                Embedding: HashTextEmbeddingService.Deserialize(reader.GetString(7))));
        }

        return results;
    }

    /// <inheritdoc />
    public Task<MemoryEntry> SaveMemoryAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken)
        => SaveMemoryCoreAsync(workspaceRoot, entry, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMemoryAsync(
        string? workspaceRoot,
        MemoryScope scope,
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenMemoryConnectionAsync(workspaceRoot, scope, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var deleted = await ExecuteDeleteByIdAsync(connection, transaction, "memory_entries", id, cancellationToken).ConfigureAwait(false);
        await ExecuteDeleteByIdAsync(connection, transaction, "memory_entries_fts", id, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntry>> ListMemoryAsync(
        string? workspaceRoot,
        MemoryScope? scope,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var entries = new List<MemoryEntry>();
        var scopes = scope is null ? new[] { MemoryScope.Project, MemoryScope.User } : [scope.Value];
        foreach (var candidateScope in scopes)
        {
            await using var connection = await OpenMemoryConnectionAsync(workspaceRoot, candidateScope, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(query))
            {
                entries.AddRange(await ListMemoryWithoutQueryAsync(connection, limit, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                entries.AddRange(await ListMemoryWithQueryAsync(connection, query!, limit, cancellationToken).ConfigureAwait(false));
            }
        }

        return entries
            .OrderByDescending(static entry => entry.UpdatedAtUtc)
            .Take(limit)
            .ToArray();
    }

    private async Task<MemoryEntry> SaveMemoryCoreAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMemoryConnectionAsync(workspaceRoot, entry.Scope, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteDeleteByIdAsync(connection, transaction, "memory_entries", entry.Id, cancellationToken).ConfigureAwait(false);
        await ExecuteDeleteByIdAsync(connection, transaction, "memory_entries_fts", entry.Id, cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO memory_entries (
                    id, scope, content, source, source_session_id, source_turn_id, tags_json, confidence,
                    related_file_path, related_symbol_name, created_at_utc, updated_at_utc, embedding)
                VALUES (
                    $id, $scope, $content, $source, $sourceSessionId, $sourceTurnId, $tagsJson, $confidence,
                    $relatedFilePath, $relatedSymbolName, $createdAtUtc, $updatedAtUtc, $embedding);
                """;
            BindMemoryParameters(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO memory_entries_fts (id, content, tags)
                VALUES ($id, $content, $tags);
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$content", entry.Content);
            command.Parameters.AddWithValue("$tags", string.Join(' ', entry.Tags));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entry;
    }

    private async Task<IReadOnlyList<MemoryEntry>> ListMemoryWithoutQueryAsync(
        SqliteConnection connection,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scope, content, source, source_session_id, source_turn_id, tags_json, confidence,
                   related_file_path, related_symbol_name, created_at_utc, updated_at_utc, embedding
            FROM memory_entries
            ORDER BY updated_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadMemoryEntriesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MemoryEntry>> ListMemoryWithQueryAsync(
        SqliteConnection connection,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeFtsQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.scope, e.content, e.source, e.source_session_id, e.source_turn_id, e.tags_json, e.confidence,
                   e.related_file_path, e.related_symbol_name, e.created_at_utc, e.updated_at_utc, e.embedding
            FROM memory_entries_fts f
            JOIN memory_entries e ON e.id = f.id
            WHERE memory_entries_fts MATCH $query
            ORDER BY bm25(memory_entries_fts)
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadMemoryEntriesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MemoryEntry>> ReadMemoryEntriesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MemoryEntry(
                Id: reader.GetString(0),
                Scope: Enum.Parse<MemoryScope>(reader.GetString(1), ignoreCase: true),
                Content: reader.GetString(2),
                Source: reader.GetString(3),
                SourceSessionId: reader.IsDBNull(4) ? null : reader.GetString(4),
                SourceTurnId: reader.IsDBNull(5) ? null : reader.GetString(5),
                Tags: System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(6)) ?? [],
                Confidence: reader.IsDBNull(7) ? null : reader.GetDouble(7),
                RelatedFilePath: reader.IsDBNull(8) ? null : reader.GetString(8),
                RelatedSymbolName: reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture)));
        }

        return results;
    }

    private static void BindMemoryParameters(SqliteCommand command, MemoryEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$scope", entry.Scope.ToString());
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$sourceSessionId", (object?)entry.SourceSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceTurnId", (object?)entry.SourceTurnId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tagsJson", System.Text.Json.JsonSerializer.Serialize(entry.Tags));
        command.Parameters.AddWithValue("$confidence", entry.Confidence is null ? DBNull.Value : entry.Confidence.Value);
        command.Parameters.AddWithValue("$relatedFilePath", (object?)entry.RelatedFilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$relatedSymbolName", (object?)entry.RelatedSymbolName ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAtUtc", entry.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$embedding", HashTextEmbeddingService.Serialize(HashTextEmbeddingService.Embed(entry.Content)));
    }

    private static async Task InsertChunkAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        IndexedWorkspaceChunk chunk,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO indexed_chunks (id, path, language, excerpt, content, start_line, end_line, embedding)
                VALUES ($id, $path, $language, $excerpt, $content, $startLine, $endLine, $embedding);
                """;
            command.Parameters.AddWithValue("$id", chunk.Id);
            command.Parameters.AddWithValue("$path", chunk.Path);
            command.Parameters.AddWithValue("$language", chunk.Language);
            command.Parameters.AddWithValue("$excerpt", chunk.Excerpt);
            command.Parameters.AddWithValue("$content", chunk.Content);
            command.Parameters.AddWithValue("$startLine", chunk.StartLine);
            command.Parameters.AddWithValue("$endLine", chunk.EndLine);
            command.Parameters.AddWithValue("$embedding", HashTextEmbeddingService.Serialize(chunk.Embedding));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO indexed_chunks_fts (id, path, excerpt, content)
                VALUES ($id, $path, $excerpt, $content);
                """;
            command.Parameters.AddWithValue("$id", chunk.Id);
            command.Parameters.AddWithValue("$path", chunk.Path);
            command.Parameters.AddWithValue("$excerpt", chunk.Excerpt);
            command.Parameters.AddWithValue("$content", chunk.Content);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertSymbolAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        IndexedWorkspaceSymbol symbol,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO indexed_symbols (id, path, name, kind, container, line, column_number)
            VALUES ($id, $path, $name, $kind, $container, $line, $column);
            """;
        command.Parameters.AddWithValue("$id", symbol.Id);
        command.Parameters.AddWithValue("$path", symbol.Path);
        command.Parameters.AddWithValue("$name", symbol.Name);
        command.Parameters.AddWithValue("$kind", symbol.Kind);
        command.Parameters.AddWithValue("$container", (object?)symbol.Container ?? DBNull.Value);
        command.Parameters.AddWithValue("$line", symbol.Line);
        command.Parameters.AddWithValue("$column", symbol.Column);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertEdgeAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        IndexedWorkspaceProjectEdge edge,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO project_edges (source_path, target, kind)
            VALUES ($sourcePath, $target, $kind);
            """;
        command.Parameters.AddWithValue("$sourcePath", edge.SourcePath);
        command.Parameters.AddWithValue("$target", edge.Target);
        command.Parameters.AddWithValue("$kind", edge.Kind);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenWorkspaceConnectionAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var dbPath = GetWorkspaceDatabasePath(workspaceRoot);
        return await OpenConnectionAsync(dbPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenMemoryConnectionAsync(
        string? workspaceRoot,
        MemoryScope scope,
        CancellationToken cancellationToken)
    {
        var dbPath = scope == MemoryScope.Project
            ? GetWorkspaceDatabasePath(workspaceRoot ?? throw new InvalidOperationException("Workspace root is required for project-scoped memory."))
            : GetUserMemoryDatabasePath();
        return await OpenConnectionAsync(dbPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(string dbPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS indexed_chunks (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL,
                language TEXT NOT NULL,
                excerpt TEXT NOT NULL,
                content TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                embedding TEXT NOT NULL
            );
            """,
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS indexed_chunks_fts USING fts5(
                id UNINDEXED,
                path,
                excerpt,
                content
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS indexed_symbols (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL,
                name TEXT NOT NULL,
                kind TEXT NOT NULL,
                container TEXT NULL,
                line INTEGER NOT NULL,
                column_number INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS project_edges (
                source_path TEXT NOT NULL,
                target TEXT NOT NULL,
                kind TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS memory_entries (
                id TEXT PRIMARY KEY,
                scope TEXT NOT NULL,
                content TEXT NOT NULL,
                source TEXT NOT NULL,
                source_session_id TEXT NULL,
                source_turn_id TEXT NULL,
                tags_json TEXT NOT NULL,
                confidence REAL NULL,
                related_file_path TEXT NULL,
                related_symbol_name TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                embedding TEXT NOT NULL
            );
            """,
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS memory_entries_fts USING fts5(
                id UNINDEXED,
                content,
                tags
            );
            """
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteDeleteByIdAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tableName,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"DELETE FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertMetadataAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO index_metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountDistinctAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(DISTINCT {columnName}) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<DateTimeOffset?> TryReadMetadataDateAsync(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM index_metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private string GetWorkspaceDatabasePath(string workspaceRoot)
    {
        var normalized = pathService.GetFullPath(workspaceRoot);
        return pathService.Combine(normalized, ".sharpclaw", WorkspaceKnowledgeDirectoryName, WorkspaceKnowledgeFileName);
    }

    private string GetUserMemoryDatabasePath()
        => pathService.Combine(userProfilePaths.GetUserSharpClawRoot(), WorkspaceKnowledgeDirectoryName, UserMemoryFileName);

    private static string NormalizeFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var tokens = query
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => new string(token.Where(static character => char.IsLetterOrDigit(character) || character == '_').ToArray()))
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(static token => token.EndsWith('*') ? token : $"{token}*");

        return string.Join(" ", tokens);
    }

    private static double ScoreSymbolHit(string query, string name, string? container)
    {
        if (string.Equals(query, name, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0.9d;
        }

        if (!string.IsNullOrWhiteSpace(container) && container.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0.65d;
        }

        return 0.55d;
    }
}
