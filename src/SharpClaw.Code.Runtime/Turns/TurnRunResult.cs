using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>
/// Represents the result of executing a single turn runner operation.
/// </summary>
/// <param name="Output">The generated output text.</param>
/// <param name="Usage">The usage snapshot for the turn.</param>
/// <param name="Summary">A concise summary of the turn outcome.</param>
/// <param name="ProviderRequest">The provider request used to produce the turn, if any.</param>
/// <param name="ProviderEvents">The streamed provider events observed during execution, if any.</param>
/// <param name="ToolResults">The tool results produced during execution, if any.</param>
/// <param name="RuntimeEvents">The runtime events emitted during agent execution, if any.</param>
/// <param name="FileMutations">Reversible file mutations performed via tools during the turn, if any.</param>
public sealed record TurnRunResult(
    string Output,
    UsageSnapshot Usage,
    string Summary,
    ProviderRequest? ProviderRequest = null,
    IReadOnlyList<ProviderEvent>? ProviderEvents = null,
    IReadOnlyList<ToolResult>? ToolResults = null,
    IReadOnlyList<RuntimeEvent>? RuntimeEvents = null,
    IReadOnlyList<FileMutationOperation>? FileMutations = null);
