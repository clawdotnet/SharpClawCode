using System.Diagnostics;
using System.Collections.Concurrent;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Hosts one terminal input loop with turn-scoped control, explicit approvals, and queued follow-ups.
/// </summary>
public sealed class ReplHost(
    ICommandRegistry commandRegistry,
    IRuntimeCommandService runtimeCommandService,
    ICustomCommandDiscoveryService customCommandDiscovery,
    ReplInteractionState replInteractionState,
    SlashCommandParser slashCommandParser,
    OutputRendererDispatcher outputRendererDispatcher,
    IReplTerminal terminal,
    ILiveTurnControlService liveTurnControl,
    ReplApprovalBroker approvalBroker) : IReplHost
{
    /// <inheritdoc />
    public async Task<int> RunAsync(CommandExecutionContext context, string? initialPrompt = null, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queued = new ConcurrentQueue<string>();
        LiveTurnHandle? activeControl = null;
        ReplApprovalSession? activeApprovals = null;
        Task<int>? active = null;
        Task<string?>? input = null;
        var inputEnded = false;
        var exitRequested = false;
        var lastExitCode = 0;

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            if (activeControl is { } running)
            {
                running.Cancel();
                terminal.WriteInfo("Cancelling the current turn; the session will remain open.");
            }
            else lifetime.Cancel();
        }

        terminal.CancelKeyPress += OnCancelKeyPress;
        try
        {
            terminal.WriteInfo("SharpClaw Code interactive mode. /help for commands; /cancel stops a turn, /steer <text> corrects it, and ordinary input queues a follow-up.");
            if (!string.IsNullOrWhiteSpace(initialPrompt)) queued.Enqueue(initialPrompt.Trim());
            while (!exitRequested || active is not null)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (active is { IsCompleted: true })
                {
                    lastExitCode = await active.ConfigureAwait(false);
                    active = null;
                }
                if (active is null && !exitRequested && queued.TryDequeue(out var next))
                    active = RunControlledPromptAsync(next);
                if (active is null && (exitRequested || inputEnded)) break;
                if (!inputEnded && !exitRequested) input ??= terminal.ReadLineAsync("sharpclaw> ", lifetime.Token).AsTask();
                if (input is null)
                {
                    if (active is not null) await active.ConfigureAwait(false);
                    continue;
                }
                if (active is not null && await Task.WhenAny(active, input).ConfigureAwait(false) == active) continue;
                var line = await input.ConfigureAwait(false);
                input = null;
                if (line is null) { inputEnded = true; activeApprovals?.CloseInput(); continue; }
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (IsExitCommand(trimmed))
                {
                    exitRequested = true;
                    queued.Clear();
                    activeControl?.Cancel();
                    continue;
                }
                if (active is not null)
                {
                    if (trimmed.Equals("/cancel", StringComparison.OrdinalIgnoreCase)) activeControl?.Cancel();
                    else if (trimmed.StartsWith("/steer ", StringComparison.OrdinalIgnoreCase))
                        terminal.WriteInfo(activeControl?.TrySteer(trimmed[7..]) == true ? "Correction queued for the next safe boundary." : "Correction could not be queued; submit it as a follow-up.");
                    else if (trimmed.StartsWith("/approve ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var valid = parts.Length == 3 && (parts[2].Equals("yes", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("no", StringComparison.OrdinalIgnoreCase));
                        terminal.WriteInfo(valid && activeApprovals?.TryRespond(parts[1], parts[2].Equals("yes", StringComparison.OrdinalIgnoreCase)) == true
                            ? "Approval response delivered." : "Use /approve <pending-id> yes|no; the ID must name a pending request.");
                    }
                    else if (trimmed.StartsWith('/')) terminal.WriteInfo("During a turn use /cancel, /steer <text>, /approve <id> yes|no, or /exit. Other commands require an idle turn.");
                    else if (queued.Count < 16) { queued.Enqueue(trimmed); terminal.WriteInfo($"Follow-up queued ({queued.Count})."); }
                    else terminal.WriteError("The follow-up queue is full (16). Wait for a turn to finish.");
                    continue;
                }
                if (trimmed.StartsWith('/')) lastExitCode = await ExecuteSlashAsync(trimmed, context, lifetime.Token).ConfigureAwait(false);
                else queued.Enqueue(trimmed);
            }
            terminal.WriteInfo("Leaving interactive mode.");
            return lastExitCode;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            activeControl?.Cancel();
            if (active is not null)
            {
                try { await active.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            lifetime.Cancel();
            if (input is not null)
            {
                try { await input.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            terminal.CancelKeyPress -= OnCancelKeyPress;
        }

        async Task<int> RunControlledPromptAsync(string prompt)
        {
            using var handle = liveTurnControl.Begin(lifetime.Token);
            using var approvals = approvalBroker.Begin();
            if (inputEnded) approvals.CloseInput();
            using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            activeControl = handle;
            activeApprovals = approvals;
            var progress = ObserveProgressAsync(handle, context.OutputFormat, progressCancellation.Token);
            try
            {
                return await ExecutePromptAsync(prompt, context, handle.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (handle.CancellationToken.IsCancellationRequested)
            {
                terminal.WriteInfo("Turn cancelled. Session state and recorded edits remain available.");
                return 0;
            }
            finally
            {
                // A correction arriving after the final safe boundary remains visible work.
                var pendingSteering = handle.CloseSteering();
                if (!exitRequested && !lifetime.IsCancellationRequested)
                    foreach (var pending in pendingSteering) queued.Enqueue(pending);
                activeControl = null;
                activeApprovals = null;
                progressCancellation.Cancel();
                try { await progress.ConfigureAwait(false); }
                catch (OperationCanceledException) when (progressCancellation.IsCancellationRequested) { }
            }
        }
    }

    private async Task ObserveProgressAsync(LiveTurnHandle handle, OutputFormat format, CancellationToken cancellationToken)
    {
        if (format == OutputFormat.Json) return;
        var elapsed = Stopwatch.StartNew();
        terminal.WriteInfo("Turn running. You can queue a follow-up or use /steer and /cancel.");
        await foreach (var envelope in handle.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            var message = envelope.Event switch
            {
                ToolStartedEvent tool => $"Running {tool.Request.ToolName}",
                ToolCompletedEvent tool => $"{tool.Result.ToolName}: {(tool.Result.Succeeded ? "completed" : "failed")}",
                ContextCompactedEvent compacted => $"Context compacted: {compacted.EstimatedTokensBefore} → {compacted.EstimatedTokensAfter} estimated tokens",
                TurnSteeredEvent => "User correction accepted",
                _ => null,
            };
            if (message is not null) terminal.WriteInfo($"[{elapsed.Elapsed.TotalSeconds:F1}s] {message}");
        }
    }

    private async Task<int> ExecuteSlashAsync(string trimmed, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var parsed = slashCommandParser.Parse(trimmed);
        if (parsed.IsSlashCommand)
        {
            if (IsExitCommand(parsed.CommandName)) return 0;

            if (string.Equals(parsed.CommandName, "help", StringComparison.OrdinalIgnoreCase))
            {
                var builtIns = commandRegistry
                    .GetSlashCommandHandlers()
                    .Select(handler => $"/{handler.CommandName} - {handler.Description}")
                    .ToList();
                var snapshot = await customCommandDiscovery.DiscoverAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
                var customs = snapshot.Commands.Select(c => $"/{c.Name} - {c.Description ?? "Custom command"}").ToList();
                var message = string.Join(
                    Environment.NewLine,
                    builtIns
                        .Prepend("/help - Lists available REPL slash commands.")
                        .Append("/cancel - Cancels only the active turn.")
                        .Append("/steer <text> - Applies a correction at the next safe boundary.")
                        .Append("/approve <id> yes|no - Responds to a pending approval.")
                        .Concat(customs)
                        .Append("/exit - Leaves interactive mode."));

                await outputRendererDispatcher.RenderCommandResultAsync(
                    new CommandResult(
                        Succeeded: true,
                        ExitCode: 0,
                        OutputFormat: context.OutputFormat,
                        Message: message,
                        DataJson: null),
                    context.OutputFormat,
                    cancellationToken);
                return 0;
            }

            var slashHandler = commandRegistry.FindSlashCommandHandler(parsed.CommandName ?? string.Empty);
            if (slashHandler is not null)
            {
                var slashExitCode = await slashHandler.ExecuteAsync(parsed, context, cancellationToken);
                if (slashExitCode != 0)
                {
                    return slashExitCode;
                }

                return 0;
            }

            var customDef = await FindCustomCommandAsync(parsed.CommandName, context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            if (customDef is not null)
            {
                try
                {
                    var argLine = string.Join(' ', parsed.Arguments);
                    var result = await runtimeCommandService
                        .ExecuteCustomCommandAsync(
                            customDef.Name,
                            argLine,
                            context.ToRuntimeCommandContext(
                                primaryModeOverride: replInteractionState.PrimaryModeOverride ?? context.PrimaryMode,
                                permissionModeOverride: replInteractionState.PermissionModeOverride ?? context.PermissionMode,
                                agentIdOverride: replInteractionState.AgentIdOverride ?? context.AgentId,
                                approvalSettingsOverride: replInteractionState.ApprovalSettingsOverride),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken);
                }
                catch (ProviderExecutionException exception)
                {
                    await outputRendererDispatcher.RenderCommandResultAsync(
                        CreateProviderFailureResult(exception, context.OutputFormat),
                        context.OutputFormat,
                        cancellationToken);
                }

                return 0;
            }

            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(
                    Succeeded: false,
                    ExitCode: 1,
                    OutputFormat: context.OutputFormat,
                    Message: $"Unknown slash command '/{parsed.CommandName}'. Type /help for available commands.",
                    DataJson: null),
                context.OutputFormat,
                cancellationToken);
            return 0;
        }

        return 0;
    }

    private async Task<CustomCommandDefinition?> FindCustomCommandAsync(
        string? name,
        string workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await customCommandDiscovery.FindAsync(workspace, name, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecutePromptAsync(string prompt, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runtimeCommandService.ExecutePromptAsync(
                prompt,
                context.ToRuntimeCommandContext(
                    primaryModeOverride: replInteractionState.PrimaryModeOverride ?? context.PrimaryMode,
                    permissionModeOverride: replInteractionState.PermissionModeOverride ?? context.PermissionMode,
                    agentIdOverride: replInteractionState.AgentIdOverride ?? context.AgentId,
                    approvalSettingsOverride: replInteractionState.ApprovalSettingsOverride),
                cancellationToken).ConfigureAwait(false);

            await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.Verification?.Status is VerificationStatus.Failed or VerificationStatus.Cancelled ? 1 : 0;
        }
        catch (ProviderExecutionException exception)
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                CreateProviderFailureResult(exception, context.OutputFormat),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }
    }

    private static CommandResult CreateProviderFailureResult(ProviderExecutionException exception, OutputFormat outputFormat)
        => new(
            Succeeded: false,
            ExitCode: 1,
            OutputFormat: outputFormat,
            Message: $"Provider failure ({exception.Kind}): {exception.Message}",
            DataJson: null);

    private static bool IsExitCommand(string? value)
        => string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/quit", StringComparison.OrdinalIgnoreCase);
}
