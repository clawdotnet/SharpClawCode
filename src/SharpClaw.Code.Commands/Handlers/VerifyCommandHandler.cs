using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Commands;

/// <summary>Shares option parsing and permission-aware verification across CLI and REPL callers.</summary>
public sealed class VerifyCommandHandler(IToolExecutor tools, ISessionStore sessions, IRuntimeHostContextAccessor host, OutputRendererDispatcher renderer, ReplInteractionState repl) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "verify";
    /// <inheritdoc />
    public string CommandName => Name;
    /// <inheritdoc />
    public string Description => "Builds/tests the .NET workspace through execution permissions, without a model.";
    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        Configure(command, (parsed, arguments, token) => ExecuteAsync(globalOptions.Resolve(parsed), arguments, isSlash: false, token));
        return command;
    }
    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var root = new RootCommand(Description);
        var effective = context with { PermissionMode = repl.PermissionModeOverride ?? context.PermissionMode, PrimaryMode = repl.PrimaryModeOverride ?? context.PrimaryMode, ApprovalSettings = repl.ApprovalSettingsOverride ?? context.ApprovalSettings };
        Configure(root, (_, arguments, token) => ExecuteAsync(effective, arguments, isSlash: true, token));
        return root.Parse(command.Arguments).InvokeAsync(cancellationToken: cancellationToken);
    }
    private static void Configure(Command command, Func<ParseResult, VerifyWorkspaceArguments, CancellationToken, Task<int>> execute)
    {
        var scope = new Option<string>("--scope") { DefaultValueFactory = _ => "affected", Description = "affected, build, tests, or all." };
        scope.AcceptOnlyFromAmong("affected", "build", "tests", "all");
        var target = new Option<string?>("--target") { Description = "Contained solution/project target." };
        var restore = new Option<bool>("--restore") { Description = "Explicitly restore before verification." };
        var configuration = new Option<string>("--configuration") { DefaultValueFactory = _ => "Debug", Description = "Build/test configuration." };
        var framework = new Option<string?>("--framework") { Description = "Optional target framework." };
        var timeout = new Option<int>("--timeout") { DefaultValueFactory = _ => 120, Description = "Per-step timeout in seconds (1–3600)." };
        timeout.Validators.Add(result => { if (result.GetValueOrDefault<int>() is < 1 or > 3600) result.AddError("--timeout must be between 1 and 3600 seconds."); });
        foreach (var option in new Option[] { scope, target, restore, configuration, framework, timeout }) command.Options.Add(option);
        command.SetAction((parsed, token) => execute(parsed, new(Enum.Parse<VerificationScope>(parsed.GetValue(scope)!, true), parsed.GetValue(target), parsed.GetValue(configuration)!, parsed.GetValue(framework), parsed.GetValue(restore), TimeoutSeconds: parsed.GetValue(timeout)), token));
    }
    private async Task<int> ExecuteAsync(CommandExecutionContext context, VerifyWorkspaceArguments arguments, bool isSlash, CancellationToken token)
    {
        using var hostScope = host.BeginScope(context.HostContext);
        var session = context.SessionId is not null ? await sessions.GetByIdAsync(context.WorkingDirectory, context.SessionId, token).ConfigureAwait(false)
            : isSlash ? await sessions.GetLatestAsync(context.WorkingDirectory, token).ConfigureAwait(false) : null;
        if (context.SessionId is not null && session is null)
        {
            await renderer.RenderCommandResultAsync(new(false, 1, context.OutputFormat, "Session was not found.", null), context.OutputFormat, token).ConfigureAwait(false);
            return 1;
        }
        var toolContext = new ToolExecutionContext(session?.Id ?? "", session?.ActiveTurnId ?? "", context.WorkingDirectory, context.WorkingDirectory, context.PermissionMode, context.OutputFormat, null,
            IsInteractive: (isSlash || !Console.IsInputRedirected) && context.OutputFormat != OutputFormat.Json, PrimaryMode: context.PrimaryMode, ApprovalSettings: context.ApprovalSettings);
        var envelope = await tools.ExecuteAsync("verify_workspace", JsonSerializer.Serialize(arguments, ProtocolJsonContext.Default.VerifyWorkspaceArguments), toolContext, token).ConfigureAwait(false);
        var result = envelope.Result;
        var commandResult = new CommandResult(result.Succeeded, result.ExitCode ?? (result.Succeeded ? 0 : 1), context.OutputFormat, result.Succeeded ? ReportSummary(result) : result.ErrorMessage ?? "Verification failed.", result.StructuredOutputJson);
        await renderer.RenderCommandResultAsync(commandResult, context.OutputFormat, token).ConfigureAwait(false);
        return commandResult.ExitCode;
    }
    private static string ReportSummary(ToolResult result) => result.StructuredOutputJson is { } json ? JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.VerificationRunReport)?.Summary ?? "Verification completed." : result.Output ?? "Verification completed.";
}
