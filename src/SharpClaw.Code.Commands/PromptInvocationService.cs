using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Executes one-shot prompt invocations, including piped stdin composition and headless routing.
/// </summary>
public sealed class PromptInvocationService(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher,
    ICliInvocationEnvironment cliInvocationEnvironment)
{
    /// <summary>
    /// Executes a prompt built from CLI arguments and optional piped stdin.
    /// </summary>
    /// <param name="promptTokens">Prompt tokens supplied on the CLI.</param>
    /// <param name="context">The command execution context.</param>
    /// <param name="forceNonInteractive">Whether the invocation must suppress approval prompts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> promptTokens,
        CommandExecutionContext context,
        bool forceNonInteractive,
        CancellationToken cancellationToken)
    {
        var prompt = await BuildPromptAsync(promptTokens, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(
                    Succeeded: false,
                    ExitCode: 1,
                    OutputFormat: context.OutputFormat,
                    Message: "No prompt text was provided. Pass text arguments or pipe input on stdin.",
                    DataJson: null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }

        var isInteractive = !forceNonInteractive
            && !cliInvocationEnvironment.IsInputRedirected
            && !cliInvocationEnvironment.IsOutputRedirected
            && context.OutputFormat == OutputFormat.Text;

        try
        {
            var result = await runtimeCommandService
                .ExecutePromptAsync(prompt, context.ToRuntimeCommandContext(isInteractive: isInteractive), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
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

    private async Task<string> BuildPromptAsync(IReadOnlyList<string> promptTokens, CancellationToken cancellationToken)
    {
        var promptText = string.Join(' ', promptTokens).Trim();
        var stdinText = cliInvocationEnvironment.IsInputRedirected
            ? (await cliInvocationEnvironment.ReadStandardInputToEndAsync(cancellationToken).ConfigureAwait(false)).Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(stdinText))
        {
            return promptText;
        }

        if (string.IsNullOrWhiteSpace(promptText))
        {
            return stdinText;
        }

        return $"Piped input:{Environment.NewLine}{stdinText}{Environment.NewLine}{Environment.NewLine}User request:{Environment.NewLine}{promptText}";
    }

    private static CommandResult CreateProviderFailureResult(ProviderExecutionException exception, OutputFormat outputFormat)
        => new(
            Succeeded: false,
            ExitCode: 1,
            OutputFormat: outputFormat,
            Message: $"Provider failure ({exception.Kind}): {exception.Message}",
            DataJson: null);
}
