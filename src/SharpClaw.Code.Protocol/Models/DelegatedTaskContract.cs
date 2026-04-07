namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Describes the explicit input and output contract for a delegated sub-agent task.
/// </summary>
/// <param name="TaskId">The delegated task identifier.</param>
/// <param name="Goal">The bounded task goal.</param>
/// <param name="ExpectedOutput">The required output contract.</param>
/// <param name="Constraints">The constraints the sub-agent must honor.</param>
public sealed record DelegatedTaskContract(
    string TaskId,
    string Goal,
    string ExpectedOutput,
    string[] Constraints);
