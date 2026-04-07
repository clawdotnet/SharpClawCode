using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Export;

/// <inheritdoc />
public sealed class SessionExportService(
    ISessionStore sessionStore,
    IEventStore eventStore,
    IPathService pathService) : ISessionExportService
{
    /// <inheritdoc />
    public async Task<(SessionExportDocument Document, string SuggestedExtension)> BuildDocumentAsync(
        string workspacePath,
        string? sessionId,
        SessionExportFormat format,
        CancellationToken cancellationToken)
    {
        var workspace = pathService.GetFullPath(workspacePath);
        var session = string.IsNullOrWhiteSpace(sessionId)
            ? await sessionStore.GetLatestAsync(workspace, cancellationToken).ConfigureAwait(false)
            : await sessionStore.GetByIdAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException("No session found to export.");
        }

        var events = await eventStore.ReadAllAsync(workspace, session.Id, cancellationToken).ConfigureAwait(false);
        var turns = BuildTurns(events);
        var eventSummary = BuildEventSummary(events);

        var document = new SessionExportDocument(
            SchemaVersion: "1.0",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Session: session,
            Turns: turns,
            SelectedEventSummary: eventSummary);

        var ext = format == SessionExportFormat.Json ? "json" : "md";
        return (document, ext);
    }

    /// <inheritdoc />
    public string RenderMarkdown(SessionExportDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Session {document.Session.Id}");
        sb.AppendLine();
        sb.AppendLine($"- **State:** {document.Session.State}");
        sb.AppendLine($"- **Created:** {document.Session.CreatedAtUtc:O}");
        sb.AppendLine($"- **Updated:** {document.Session.UpdatedAtUtc:O}");
        sb.AppendLine();

        foreach (var turn in document.Turns.OrderBy(t => t.SequenceNumber))
        {
            sb.AppendLine($"## Turn {turn.SequenceNumber} ({turn.TurnId})");
            if (!string.IsNullOrWhiteSpace(turn.Input))
            {
                sb.AppendLine("### Input");
                sb.AppendLine(turn.Input);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(turn.Output))
            {
                sb.AppendLine("### Output");
                sb.AppendLine(turn.Output);
                sb.AppendLine();
            }

            if (turn.NotableToolActions.Count > 0)
            {
                sb.AppendLine("### Tools");
                foreach (var tool in turn.NotableToolActions)
                {
                    sb.AppendLine($"- `{tool.ToolName}` ({(tool.Succeeded ? "ok" : "fail")}): {tool.Summary}");
                }

                sb.AppendLine();
            }
        }

        if (document.SelectedEventSummary is { Count: > 0 } tail)
        {
            sb.AppendLine("## Recent events");
            foreach (var line in tail)
            {
                sb.AppendLine($"- {line}");
            }
        }

        return sb.ToString();
    }

    private static List<SessionExportTurn> BuildTurns(IReadOnlyList<RuntimeEvent> events)
    {
        var completedByTurn = events.OfType<TurnCompletedEvent>().ToDictionary(e => e.Turn.Id, e => e.Turn, StringComparer.Ordinal);
        var toolActions = events.OfType<ToolCompletedEvent>()
            .GroupBy(t => t.TurnId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var started = events.OfType<TurnStartedEvent>().Select(e => e.Turn).OrderBy(t => t.SequenceNumber).ToArray();
        var rows = new List<SessionExportTurn>();
        foreach (var turn in started)
        {
            var outputTurn = completedByTurn.TryGetValue(turn.Id, out var done) ? done : turn;
            var tools = toolActions.TryGetValue(turn.Id, out var list)
                ? list.Select(MapTool).ToList()
                : [];

            rows.Add(new SessionExportTurn(
                TurnId: turn.Id,
                SequenceNumber: turn.SequenceNumber,
                Input: turn.Input,
                Output: outputTurn.Output,
                NotableToolActions: tools));
        }

        return rows;
    }

    private static SessionExportToolAction MapTool(ToolCompletedEvent completed)
        => new(
            completed.TurnId ?? string.Empty,
            completed.Result.ToolName,
            completed.Result.Succeeded,
            completed.Result.ErrorMessage ?? completed.Result.Output);

    private static List<string> BuildEventSummary(IReadOnlyList<RuntimeEvent> events)
    {
        const int max = 12;
        return events.TakeLast(Math.Min(max, events.Count))
            .Select(Summarize)
            .ToList();
    }

    private static string Summarize(RuntimeEvent @event)
        => @event switch
        {
            SessionForkedEvent fork => $"sessionForked parent={fork.ParentSessionId}",
            TurnStartedEvent => $"turnStarted {((TurnStartedEvent)@event).Turn.Id}",
            TurnCompletedEvent done => $"turnCompleted {done.Turn.Id} ok={done.Succeeded}",
            ToolCompletedEvent tool => $"toolCompleted {tool.Result.ToolName}",
            _ => @event.GetType().Name,
        };
}
