using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores evolution proposals in the workspace SQLite catalog.
/// </summary>
public sealed class SqliteEvolutionProposalStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : IEvolutionProposalStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EvolutionProposal>> ListAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM evolution_proposals
            ORDER BY updated_at_utc DESC;
            """;

        var items = new List<EvolutionProposal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                var item = JsonSerializer.Deserialize(reader.GetString(0), ProtocolJsonContext.Default.EvolutionProposal);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<EvolutionProposal?> GetByIdAsync(string workspacePath, string proposalId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM evolution_proposals WHERE proposal_id = $proposalId LIMIT 1;";
        command.Parameters.AddWithValue("$proposalId", proposalId);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.EvolutionProposal);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, EvolutionProposal proposal, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO evolution_proposals(proposal_id, updated_at_utc, status, payload_json)
            VALUES ($proposalId, $updatedAtUtc, $status, $payloadJson)
            ON CONFLICT(proposal_id) DO UPDATE SET
                updated_at_utc = excluded.updated_at_utc,
                status = excluded.status,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$proposalId", proposal.Id);
        command.Parameters.AddWithValue("$updatedAtUtc", (proposal.UpdatedAtUtc ?? proposal.CreatedAtUtc).ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$status", proposal.Status.ToString());
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(proposal, ProtocolJsonContext.Default.EvolutionProposal));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string workspacePath, string proposalId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSessionStoreDatabase
            .OpenConnectionAsync(fileSystem, storagePathResolver, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM evolution_proposals WHERE proposal_id = $proposalId;";
        command.Parameters.AddWithValue("$proposalId", proposalId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }
}
