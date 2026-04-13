using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Produces a compact durable summary from persisted runtime events.
/// </summary>
public sealed class ConversationCompactionService(
    ISessionStore sessionStore,
    IEventStore eventStore,
    ISystemClock systemClock) : IConversationCompactionService
{
    /// <inheritdoc />
    public async Task<(ConversationSession Session, string Summary)> CompactAsync(
        string workspaceRoot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetByIdAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var events = await eventStore.ReadAllAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);

        var summary = BuildSummary(events);
        var title = BuildTitle(events, session.Title);
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata[SharpClawWorkflowMetadataKeys.CompactedSummary] = summary;

        var updated = session with
        {
            Title = title,
            UpdatedAtUtc = systemClock.UtcNow,
            Metadata = metadata,
        };

        await sessionStore.SaveAsync(workspaceRoot, updated, cancellationToken).ConfigureAwait(false);
        return (updated, summary);
    }

    private static string BuildSummary(IReadOnlyList<RuntimeEvent> events)
    {
        var userPrompts = events.OfType<TurnStartedEvent>().Select(static e => e.Turn.Input).Where(static text => !string.IsNullOrWhiteSpace(text)).TakeLast(4).ToArray();
        var outputs = events.OfType<TurnCompletedEvent>().Select(static e => e.Turn.Output).Where(static text => !string.IsNullOrWhiteSpace(text)).TakeLast(3).ToArray();
        var toolNames = events.OfType<ToolCompletedEvent>().Select(static e => e.Result.ToolName).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();

        var builder = new StringBuilder();
        if (userPrompts.Length > 0)
        {
            builder.Append("Recent requests: ");
            builder.Append(string.Join(" | ", userPrompts.Select(TrimSentence)));
            builder.Append(". ");
        }

        if (outputs.Length > 0)
        {
            builder.Append("Recent outcomes: ");
            builder.Append(string.Join(" | ", outputs.Select(TrimSentence)));
            builder.Append(". ");
        }

        if (toolNames.Length > 0)
        {
            builder.Append("Tools used: ");
            builder.Append(string.Join(", ", toolNames));
            builder.Append('.');
        }

        return builder.Length == 0
            ? "No completed turns yet."
            : builder.ToString().Trim();
    }

    private static string BuildTitle(IReadOnlyList<RuntimeEvent> events, string fallback)
    {
        var firstPrompt = events.OfType<TurnStartedEvent>().Select(static e => e.Turn.Input).FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
        if (string.IsNullOrWhiteSpace(firstPrompt))
        {
            return fallback;
        }

        var trimmed = TrimSentence(firstPrompt);
        return trimmed.Length <= 72 ? trimmed : trimmed[..72].TrimEnd() + "...";
    }

    private static string TrimSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return collapsed.Length <= 120 ? collapsed : collapsed[..120].TrimEnd() + "...";
    }
}
