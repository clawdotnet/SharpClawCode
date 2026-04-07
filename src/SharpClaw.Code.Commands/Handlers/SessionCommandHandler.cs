using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Inspects durable session state, forks sessions, and exports transcripts.
/// </summary>
public sealed class SessionCommandHandler(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "session";

    /// <inheritdoc />
    public string Description => "Inspects, forks, or exports persisted sessions.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        var show = new Command("show", "Shows the latest session or a session by id.");
        var idOption = new Option<string?>("--id")
        {
            Description = "Session id. When omitted, the latest session in the workspace is used."
        };
        show.Options.Add(idOption);
        show.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var id = parseResult.GetValue(idOption);
            var result = await runtimeCommandService
                .InspectSessionAsync(id, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(show);

        var fork = new Command("fork", "Creates a child session linked to an existing session.");
        var forkId = new Option<string?>("--id")
        {
            Description = "Parent session id. Latest is used when omitted."
        };
        fork.Options.Add(forkId);
        fork.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var id = parseResult.GetValue(forkId);
            var result = await runtimeCommandService
                .ForkSessionAsync(id, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(fork);

        var export = new Command("export", "Exports session content to Markdown or JSON.");
        var exportId = new Option<string?>("--id") { Description = "Session id; latest when omitted." };
        var format = new Option<string>("--format")
        {
            Description = "md or json.",
            DefaultValueFactory = _ => "md",
        };
        var outPath = new Option<string?>("--out") { Description = "Optional explicit output file path." };
        export.Options.Add(exportId);
        export.Options.Add(format);
        export.Options.Add(outPath);
        export.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var sid = parseResult.GetValue(exportId);
            var fmtText = parseResult.GetValue(format) ?? "md";
            var fmt = fmtText.Trim().Equals("json", StringComparison.OrdinalIgnoreCase)
                ? SessionExportFormat.Json
                : SessionExportFormat.Markdown;
            var path = parseResult.GetValue(outPath);
            var result = await runtimeCommandService
                .ExportSessionAsync(sid, fmt, path, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(export);

        var list = new Command("list", "Lists persisted sessions for the workspace.");
        list.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await runtimeCommandService
                .ListSessionsAsync(ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(list);

        var attach = new Command("attach", "Sets the workspace's attached session for subsequent prompts.");
        var attachId = new Option<string>("--id") { Required = true, Description = "Session id to attach." };
        attach.Options.Add(attachId);
        attach.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var sid = parseResult.GetValue(attachId) ?? throw new InvalidOperationException("--id is required.");
            var result = await runtimeCommandService
                .AttachSessionAsync(sid, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(attach);

        var detach = new Command("detach", "Clears explicit workspace session attachment.");
        detach.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await runtimeCommandService
                .DetachSessionAsync(ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(detach);

        var bundle = new Command("bundle", "Exports a portable zip bundle (offline sharing).");
        var bundleId = new Option<string?>("--id") { Description = "Session id; latest when omitted." };
        var bundleOut = new Option<string?>("--out") { Description = "Optional explicit .zip output path." };
        bundle.Options.Add(bundleId);
        bundle.Options.Add(bundleOut);
        bundle.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var sid = parseResult.GetValue(bundleId);
            var outp = parseResult.GetValue(bundleOut);
            var result = await runtimeCommandService
                .ExportPortableSessionBundleAsync(sid, outp, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(bundle);

        var import = new Command("import", "Imports a portable zip bundle into this workspace.");
        var importFrom = new Option<string>("--from") { Required = true, Description = "Path to .sharpclaw-bundle.zip." };
        var importReplace = new Option<bool>("--replace")
        {
            Description = "Replace existing session directory with the same id.",
            DefaultValueFactory = _ => false,
        };
        var importAttach = new Option<bool>("--attach")
        {
            Description = "Attach the imported session for subsequent prompts.",
            DefaultValueFactory = _ => false,
        };
        import.Options.Add(importFrom);
        import.Options.Add(importReplace);
        import.Options.Add(importAttach);
        import.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var from = parseResult.GetValue(importFrom) ?? throw new InvalidOperationException("--from is required.");
            var replace = parseResult.GetValue(importReplace);
            var attach = parseResult.GetValue(importAttach);
            var result = await runtimeCommandService
                .ImportPortableSessionBundleAsync(from, replace, attach, ToRuntimeContext(context), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        command.Subcommands.Add(import);

        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0)
        {
            return ExecuteInspectAsync(null, context, cancellationToken);
        }

        var verb = command.Arguments[0];
        if (string.Equals(verb, "list", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteListAsync(context, cancellationToken);
        }

        if (string.Equals(verb, "attach", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Arguments.Length < 2)
            {
                return RenderAsync("Usage: /session attach <sessionId>", context, false, cancellationToken);
            }

            return ExecuteAttachAsync(command.Arguments[1], context, cancellationToken);
        }

        if (string.Equals(verb, "detach", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteDetachAsync(context, cancellationToken);
        }

        if (string.Equals(verb, "bundle", StringComparison.OrdinalIgnoreCase))
        {
            var sid = command.Arguments.Length > 1 ? command.Arguments[1] : null;
            return ExecuteBundleAsync(sid, null, context, cancellationToken);
        }

        if (string.Equals(verb, "import", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Arguments.Length < 2)
            {
                return RenderAsync("Usage: /session import <pathToZip>", context, false, cancellationToken);
            }

            return ExecuteImportAsync(command.Arguments[1], false, false, context, cancellationToken);
        }

        if (string.Equals(verb, "fork", StringComparison.OrdinalIgnoreCase))
        {
            var sid = command.Arguments.Length > 1 ? command.Arguments[1] : null;
            return ExecuteForkAsync(sid, context, cancellationToken);
        }

        if (string.Equals(verb, "export", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Arguments.Length < 2)
            {
                return RenderAsync("Usage: /session export md|json [sessionId]", context, false, cancellationToken);
            }

            var fmt = command.Arguments[1].Trim().ToLowerInvariant() switch
            {
                "json" => SessionExportFormat.Json,
                _ => SessionExportFormat.Markdown,
            };
            var exportSessionId = command.Arguments.Length > 2 ? command.Arguments[2] : null;
            return ExecuteExportAsync(exportSessionId, fmt, context, cancellationToken);
        }

        var sessionId = command.Arguments.Length > 0 ? command.Arguments[0] : null;
        return ExecuteInspectAsync(sessionId, context, cancellationToken);
    }

    private async Task<int> ExecuteInspectAsync(string? sessionId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .InspectSessionAsync(sessionId, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteForkAsync(string? sourceId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ForkSessionAsync(sourceId, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ListSessionsAsync(ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteAttachAsync(string sessionId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .AttachSessionAsync(sessionId, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteDetachAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .DetachSessionAsync(ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteBundleAsync(
        string? sessionId,
        string? outputPath,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ExportPortableSessionBundleAsync(sessionId, outputPath, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteImportAsync(
        string bundleZipPath,
        bool replaceExisting,
        bool attachAfterImport,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ImportPortableSessionBundleAsync(bundleZipPath, replaceExisting, attachAfterImport, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> ExecuteExportAsync(
        string? sessionId,
        SessionExportFormat format,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ExportSessionAsync(sessionId, format, null, ToRuntimeContext(context), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> RenderAsync(string message, CommandExecutionContext context, bool success, CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }

    private static RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.SessionId);
}
