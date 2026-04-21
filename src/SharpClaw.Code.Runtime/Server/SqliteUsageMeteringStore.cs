using System.Globalization;
using Microsoft.Data.Sqlite;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Server;

/// <summary>
/// Persists normalized usage metering records in a tenant-aware SQLite store.
/// </summary>
public sealed class SqliteUsageMeteringStore(
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver) : IUsageMeteringStore
{
    /// <inheritdoc />
    public async Task AppendAsync(string workspaceRoot, UsageMeteringRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO usage_records (
                id, kind, occurred_at_utc, tenant_id, host_id, workspace_root, session_id, turn_id,
                provider_name, model, tool_name, approval_scope, succeeded, duration_ms,
                input_tokens, output_tokens, cached_input_tokens, total_tokens, estimated_cost_usd, detail)
            VALUES (
                $id, $kind, $occurredAtUtc, $tenantId, $hostId, $workspaceRoot, $sessionId, $turnId,
                $providerName, $model, $toolName, $approvalScope, $succeeded, $durationMs,
                $inputTokens, $outputTokens, $cachedInputTokens, $totalTokens, $estimatedCostUsd, $detail);
            """;
        BindRecord(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UsageMeteringSummaryReport> GetSummaryAsync(string workspaceRoot, UsageMeteringQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var whereClause = BuildWhereClause(query, out var parameters);
        var sql = $"""
            SELECT
                COALESCE(SUM(CASE WHEN kind = 'ProviderUsage' THEN input_tokens ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'ProviderUsage' THEN output_tokens ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'ProviderUsage' THEN cached_input_tokens ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'ProviderUsage' THEN total_tokens ELSE 0 END), 0),
                SUM(CASE WHEN kind = 'ProviderUsage' THEN estimated_cost_usd END),
                COALESCE(SUM(CASE WHEN kind = 'ProviderUsage' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'ToolExecution' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'TurnExecution' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN kind = 'SessionLifecycle' THEN 1 ELSE 0 END), 0)
            FROM usage_records
            {whereClause};
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        decimal? estimatedCost = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
        return new UsageMeteringSummaryReport(
            query,
            new UsageSnapshot(
                checked((int)reader.GetInt64(0)),
                checked((int)reader.GetInt64(1)),
                checked((int)reader.GetInt64(2)),
                checked((int)reader.GetInt64(3)),
                estimatedCost),
            checked((int)reader.GetInt64(5)),
            checked((int)reader.GetInt64(6)),
            checked((int)reader.GetInt64(7)),
            checked((int)reader.GetInt64(8)));
    }

    /// <inheritdoc />
    public async Task<UsageMeteringDetailReport> GetDetailAsync(
        string workspaceRoot,
        UsageMeteringQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var whereClause = BuildWhereClause(query, out var parameters);
        var sql = $"""
            SELECT
                id, kind, occurred_at_utc, tenant_id, host_id, workspace_root, session_id, turn_id,
                provider_name, model, tool_name, approval_scope, succeeded, duration_ms,
                input_tokens, output_tokens, cached_input_tokens, total_tokens, estimated_cost_usd, detail
            FROM usage_records
            {whereClause}
            ORDER BY occurred_at_utc DESC, id DESC
            LIMIT $limit;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        command.Parameters.AddWithValue("$limit", limit);

        var records = new List<UsageMeteringRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new UsageMeteringRecord(
                Id: reader.GetString(0),
                Kind: Enum.Parse<UsageMeteringRecordKind>(reader.GetString(1), ignoreCase: true),
                OccurredAtUtc: DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                TenantId: reader.IsDBNull(3) ? null : reader.GetString(3),
                HostId: reader.IsDBNull(4) ? null : reader.GetString(4),
                WorkspaceRoot: reader.IsDBNull(5) ? null : reader.GetString(5),
                SessionId: reader.IsDBNull(6) ? null : reader.GetString(6),
                TurnId: reader.IsDBNull(7) ? null : reader.GetString(7),
                ProviderName: reader.IsDBNull(8) ? null : reader.GetString(8),
                Model: reader.IsDBNull(9) ? null : reader.GetString(9),
                ToolName: reader.IsDBNull(10) ? null : reader.GetString(10),
                ApprovalScope: reader.IsDBNull(11) ? null : Enum.Parse<SharpClaw.Code.Protocol.Enums.ApprovalScope>(reader.GetString(11), ignoreCase: true),
                Succeeded: reader.IsDBNull(12) ? null : reader.GetInt64(12) != 0,
                DurationMilliseconds: reader.IsDBNull(13) ? null : reader.GetInt64(13),
                Usage: HasUsage(reader)
                    ? new UsageSnapshot(
                        reader.GetInt32(14),
                        reader.GetInt32(15),
                        reader.GetInt32(16),
                        reader.GetInt32(17),
                        reader.IsDBNull(18) ? null : reader.GetDecimal(18))
                    : null,
                Detail: reader.IsDBNull(19) ? null : reader.GetString(19)));
        }

        return new UsageMeteringDetailReport(query, records);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var dbPath = storagePathResolver.GetUsageMeteringDatabasePath(normalizedWorkspace);
        var telemetryRoot = Path.GetDirectoryName(dbPath)
            ?? storagePathResolver.GetTelemetryRoot(normalizedWorkspace);
        fileSystem.CreateDirectory(telemetryRoot);

        var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS usage_records (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                tenant_id TEXT NULL,
                host_id TEXT NULL,
                workspace_root TEXT NULL,
                session_id TEXT NULL,
                turn_id TEXT NULL,
                provider_name TEXT NULL,
                model TEXT NULL,
                tool_name TEXT NULL,
                approval_scope TEXT NULL,
                succeeded INTEGER NULL,
                duration_ms INTEGER NULL,
                input_tokens INTEGER NULL,
                output_tokens INTEGER NULL,
                cached_input_tokens INTEGER NULL,
                total_tokens INTEGER NULL,
                estimated_cost_usd REAL NULL,
                detail TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_usage_records_occurred_at ON usage_records(occurred_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_usage_records_session ON usage_records(session_id);
            CREATE INDEX IF NOT EXISTS ix_usage_records_tenant_host ON usage_records(tenant_id, host_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildWhereClause(UsageMeteringQuery query, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var conditions = new List<string>();
        if (query.FromUtc is { } fromUtc)
        {
            conditions.Add("occurred_at_utc >= $fromUtc");
            parameters["$fromUtc"] = fromUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        if (query.ToUtc is { } toUtc)
        {
            conditions.Add("occurred_at_utc <= $toUtc");
            parameters["$toUtc"] = toUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(query.TenantId))
        {
            conditions.Add("tenant_id = $tenantId");
            parameters["$tenantId"] = query.TenantId!;
        }

        if (!string.IsNullOrWhiteSpace(query.HostId))
        {
            conditions.Add("host_id = $hostId");
            parameters["$hostId"] = query.HostId!;
        }

        if (!string.IsNullOrWhiteSpace(query.WorkspaceRoot))
        {
            conditions.Add("workspace_root = $workspaceRoot");
            parameters["$workspaceRoot"] = query.WorkspaceRoot!;
        }

        if (!string.IsNullOrWhiteSpace(query.SessionId))
        {
            conditions.Add("session_id = $sessionId");
            parameters["$sessionId"] = query.SessionId!;
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static void AddParameters(SqliteCommand command, IReadOnlyDictionary<string, object> parameters)
    {
        foreach (var pair in parameters)
        {
            command.Parameters.AddWithValue(pair.Key, pair.Value);
        }
    }

    private static void BindRecord(SqliteCommand command, UsageMeteringRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$kind", record.Kind.ToString());
        command.Parameters.AddWithValue("$occurredAtUtc", record.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$tenantId", (object?)record.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("$hostId", (object?)record.HostId ?? DBNull.Value);
        command.Parameters.AddWithValue("$workspaceRoot", (object?)record.WorkspaceRoot ?? DBNull.Value);
        command.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$turnId", (object?)record.TurnId ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)record.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)record.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$toolName", (object?)record.ToolName ?? DBNull.Value);
        command.Parameters.AddWithValue("$approvalScope", record.ApprovalScope?.ToString() is { } scope ? scope : DBNull.Value);
        command.Parameters.AddWithValue("$succeeded", record.Succeeded.HasValue ? record.Succeeded.Value ? 1 : 0 : DBNull.Value);
        command.Parameters.AddWithValue("$durationMs", record.DurationMilliseconds.HasValue ? record.DurationMilliseconds.Value : DBNull.Value);
        command.Parameters.AddWithValue("$inputTokens", record.Usage?.InputTokens is { } input ? input : DBNull.Value);
        command.Parameters.AddWithValue("$outputTokens", record.Usage?.OutputTokens is { } output ? output : DBNull.Value);
        command.Parameters.AddWithValue("$cachedInputTokens", record.Usage?.CachedInputTokens is { } cached ? cached : DBNull.Value);
        command.Parameters.AddWithValue("$totalTokens", record.Usage?.TotalTokens is { } total ? total : DBNull.Value);
        command.Parameters.AddWithValue("$estimatedCostUsd", record.Usage?.EstimatedCostUsd is { } cost ? cost : DBNull.Value);
        command.Parameters.AddWithValue("$detail", (object?)record.Detail ?? DBNull.Value);
    }

    private static bool HasUsage(SqliteDataReader reader)
        => !reader.IsDBNull(14)
            || !reader.IsDBNull(15)
            || !reader.IsDBNull(16)
            || !reader.IsDBNull(17)
            || !reader.IsDBNull(18);
}
