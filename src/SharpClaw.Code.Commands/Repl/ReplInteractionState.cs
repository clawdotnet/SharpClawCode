using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Holds per-process REPL overrides layered on top of CLI-resolved options.
/// </summary>
public sealed class ReplInteractionState
{
    /// <summary>
    /// When set, wins over <see cref="Models.CommandExecutionContext.PrimaryMode"/> for REPL turns.
    /// </summary>
    public PrimaryMode? PrimaryModeOverride { get; set; }

    /// <summary>
    /// When set, wins over <see cref="Models.CommandExecutionContext.AgentId"/> for REPL turns.
    /// </summary>
    public string? AgentIdOverride { get; set; }

    /// <summary>
    /// When set, wins over <see cref="Models.CommandExecutionContext.ApprovalSettings"/> for REPL turns.
    /// </summary>
    public ApprovalSettings? ApprovalSettingsOverride { get; set; }
}
