using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Implements the prompt command.
/// </summary>
public sealed class PromptCommandHandler(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "prompt";

    /// <inheritdoc />
    public string Description => "Runs a single prompt against the runtime.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        var promptArgument = new Argument<string[]>("text")
        {
            Description = "The prompt text to execute."
        };

        command.Arguments.Add(promptArgument);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var prompt = string.Join(' ', parseResult.GetValue(promptArgument) ?? []).Trim();
            var context = globalOptions.Resolve(parseResult);
            try
            {
                var result = await runtimeCommandService.ExecutePromptAsync(prompt, ToRuntimeContext(context), cancellationToken);
                await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken);
                return 0;
            }
            catch (ProviderExecutionException exception)
            {
                await outputRendererDispatcher.RenderCommandResultAsync(
                    CreateProviderFailureResult(exception, context.OutputFormat),
                    context.OutputFormat,
                    cancellationToken);
                return 1;
            }
        });

        return command;
    }

    private static RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.SessionId);

    private static CommandResult CreateProviderFailureResult(ProviderExecutionException exception, OutputFormat outputFormat)
        => new(
            Succeeded: false,
            ExitCode: 1,
            OutputFormat: outputFormat,
            Message: $"Provider failure ({exception.Kind}): {exception.Message}",
            DataJson: null);
}
