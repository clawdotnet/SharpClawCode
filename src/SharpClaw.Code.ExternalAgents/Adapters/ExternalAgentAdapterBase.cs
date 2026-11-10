using SharpClaw.Code.ExternalAgents.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.ExternalAgents.Adapters;

/// <summary>
/// Base implementation for text-mode CLI adapters.
/// </summary>
public abstract class ExternalAgentAdapterBase(
    IExternalAgentExecutableResolver executableResolver,
    IProcessRunner processRunner,
    IExternalAgentConfigProvider configProvider) : IExternalAgentAdapter
{
    /// <summary>Deadline for one external agent run; delegated agents routinely run far longer than a shell command.</summary>
    protected static readonly TimeSpan ExternalAgentTimeout = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public abstract ExternalAgentDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the configured adapter executable path.
    /// </summary>
    protected virtual string ResolveExecutable(ExternalAgentAdapterConfig? config) => config?.ExecutablePath ?? Descriptor.ExecutableName;

    /// <summary>
    /// Builds process arguments.
    /// </summary>
    protected abstract string[] BuildArguments(ExternalAgentRunRequest request);

    /// <inheritdoc />
    public virtual async Task<ExternalAgentStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var config = await configProvider.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var adapterConfig = FindAdapterConfig(config);
        var enabled = config.Enabled && (adapterConfig?.Enabled ?? true);
        if (!enabled)
        {
            return new ExternalAgentStatus(Descriptor, ExternalAgentHealth.Disabled, false, null, "Adapter is disabled by configuration.");
        }

        var path = executableResolver.Resolve(ResolveExecutable(adapterConfig));
        return new ExternalAgentStatus(
            Descriptor,
            path is null ? ExternalAgentHealth.Missing : ExternalAgentHealth.Available,
            Enabled: enabled,
            ExecutablePath: path,
            Detail: path is null ? $"Executable '{ResolveExecutable(adapterConfig)}' was not found." : "Executable found.");
    }

    /// <inheritdoc />
    public async Task<ExternalAgentRunResult> RunAsync(ExternalAgentRunRequest request, CancellationToken cancellationToken)
    {
        var config = await configProvider.GetConfigAsync(request.WorkspacePath, cancellationToken).ConfigureAwait(false);
        var adapterConfig = FindAdapterConfig(config);
        var enabled = config.Enabled && (adapterConfig?.Enabled ?? true);
        if (!enabled)
        {
            return Failure(request.AdapterId, ExternalAgentFailureKind.Disabled, "External agent adapter is disabled.");
        }

        var executableNameOrPath = ResolveExecutable(adapterConfig);
        var executable = executableResolver.Resolve(executableNameOrPath);
        if (executable is null)
        {
            return Failure(request.AdapterId, ExternalAgentFailureKind.ExecutableMissing, $"Executable '{executableNameOrPath}' was not found.");
        }

        try
        {
            var result = await processRunner
                .RunAsync(
                    new ProcessRunRequest(
                        executable,
                        MergeArguments(adapterConfig, request),
                        request.WorkspacePath,
                        request.Environment,
                        ExternalAgentTimeout),
                    cancellationToken)
                .ConfigureAwait(false);
            var output = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError
                : string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardOutput + Environment.NewLine + result.StandardError;
            var failure = result.ExitCode == 0 ? ExternalAgentFailureKind.None : ExternalAgentFailureKind.ProcessFailed;
            return new ExternalAgentRunResult(
                request.AdapterId,
                result.ExitCode,
                output,
                [],
                null,
                failure,
                failure == ExternalAgentFailureKind.None ? null : Truncate(result.StandardError));
        }
        catch (OperationCanceledException)
        {
            return Failure(request.AdapterId, ExternalAgentFailureKind.Cancelled, "External agent run was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure(request.AdapterId, ExternalAgentFailureKind.Unexpected, ex.Message);
        }
    }

    /// <summary>
    /// Creates a stable failed result.
    /// </summary>
    protected static ExternalAgentRunResult Failure(string adapterId, ExternalAgentFailureKind kind, string error)
        => new(adapterId, 1, string.Empty, [], null, kind, error);

    private ExternalAgentAdapterConfig? FindAdapterConfig(ExternalAgentsConfig config)
        => config.Adapters is not null && config.Adapters.TryGetValue(Descriptor.Id, out var adapterConfig)
            ? adapterConfig
            : null;

    private string[] MergeArguments(ExternalAgentAdapterConfig? adapterConfig, ExternalAgentRunRequest request)
    {
        var defaultArgs = adapterConfig?.DefaultArgs ?? [];
        var requestArgs = request.AdditionalArgs ?? [];
        return [.. defaultArgs, .. BuildArguments(request), .. requestArgs];
    }

    /// <summary>
    /// Truncates text for operational summaries.
    /// </summary>
    protected static string Truncate(string? value, int max = 800)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
