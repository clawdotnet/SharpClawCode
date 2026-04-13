using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Exposes the runtime operations required by the initial CLI vertical slice.
/// </summary>
public interface IRuntimeCommandService
{
    /// <summary>
    /// Executes a prompt through the runtime.
    /// </summary>
    /// <param name="prompt">The prompt text to execute.</param>
    /// <param name="context">The invocation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The turn execution result.</returns>
    Task<TurnExecutionResult> ExecutePromptAsync(string prompt, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a discovered markdown custom command through the normal prompt pipeline.
    /// </summary>
    /// <param name="commandName">Command file stem / name.</param>
    /// <param name="arguments">Arguments passed to the template expander.</param>
    /// <param name="context">The invocation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The turn execution result.</returns>
    Task<TurnExecutionResult> ExecuteCustomCommandAsync(
        string commandName,
        string arguments,
        RuntimeCommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current runtime status.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A command result describing runtime status.</returns>
    Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the initial doctor checks for the runtime.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A command result describing the diagnostic outcome.</returns>
    Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Inspects a durable session (latest when <paramref name="sessionId"/> is null).
    /// </summary>
    /// <param name="sessionId">The session id, or null for latest.</param>
    /// <param name="context">The invocation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A command result with <see cref="SessionInspectionReport" /> JSON when found.</returns>
    Task<CommandResult> InspectSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Forks a session, producing a new child with lineage metadata.
    /// </summary>
    /// <param name="sourceSessionId">The parent session id, or null to use latest.</param>
    /// <param name="context">Invocation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command result with forked session JSON.</returns>
    Task<CommandResult> ForkSessionAsync(string? sourceSessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Exports session content to Markdown or JSON.
    /// </summary>
    /// <param name="sessionId">Session id or null for latest.</param>
    /// <param name="format">Export format.</param>
    /// <param name="outputFilePath">Optional explicit output path.</param>
    /// <param name="context">Invocation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command result with path and payload info.</returns>
    Task<CommandResult> ExportSessionAsync(
        string? sessionId,
        SessionExportFormat format,
        string? outputFilePath,
        RuntimeCommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Undoes the last SharpClaw-tracked mutation set for the session (or latest attached session).
    /// </summary>
    Task<CommandResult> UndoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Redoes the last undone mutation set.
    /// </summary>
    Task<CommandResult> RedoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Exports a portable zip bundle containing session payload and manifest.
    /// </summary>
    Task<CommandResult> ExportPortableSessionBundleAsync(
        string? sessionId,
        string? outputZipPath,
        RuntimeCommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports a portable session bundle zip into the workspace.
    /// </summary>
    Task<CommandResult> ImportPortableSessionBundleAsync(
        string bundleZipPath,
        bool replaceExisting,
        bool attachAfterImport,
        RuntimeCommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists sessions for the workspace with attachment markers.
    /// </summary>
    Task<CommandResult> ListSessionsAsync(RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Attaches an explicit session id for subsequent prompts.
    /// </summary>
    Task<CommandResult> AttachSessionAsync(string sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Clears explicit workspace session attachment.
    /// </summary>
    Task<CommandResult> DetachSessionAsync(RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or refreshes a self-hosted share snapshot for a session.
    /// </summary>
    Task<CommandResult> ShareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a self-hosted share snapshot for a session.
    /// </summary>
    Task<CommandResult> UnshareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Compacts a session into a reusable summary and refreshed title.
    /// </summary>
    Task<CommandResult> CompactSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents normalized command invocation context passed into the runtime layer.
/// </summary>
/// <param name="WorkingDirectory">The working directory for the invocation.</param>
/// <param name="Model">The selected model, if any.</param>
/// <param name="PermissionMode">The effective permission mode.</param>
/// <param name="OutputFormat">The requested output format.</param>
/// <param name="PrimaryMode">Optional primary mode; when null the session/request default applies.</param>
/// <param name="SessionId">Optional explicit session id (e.g. from <c>--session</c>); when null, attachment/latest resolution applies.</param>
/// <param name="AgentId">Optional effective agent id.</param>
public sealed record RuntimeCommandContext(
    string WorkingDirectory,
    string? Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    PrimaryMode? PrimaryMode = null,
    string? SessionId = null,
    string? AgentId = null);
