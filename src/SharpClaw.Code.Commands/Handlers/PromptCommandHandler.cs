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
    PromptInvocationService promptInvocationService) : ICommandHandler
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
            Description = "The prompt text to execute. When omitted, stdin is used if redirected.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        command.Arguments.Add(promptArgument);
        command.SetAction((parseResult, cancellationToken) =>
            promptInvocationService.ExecuteAsync(
                parseResult.GetValue(promptArgument) ?? [],
                globalOptions.Resolve(parseResult),
                forceNonInteractive: false,
                cancellationToken));

        return command;
    }
}
