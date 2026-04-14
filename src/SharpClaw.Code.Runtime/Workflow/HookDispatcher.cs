using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Executes configured hook processes for runtime lifecycle triggers.
/// </summary>
public sealed class HookDispatcher(
    ISharpClawConfigService configService,
    IProcessRunner processRunner,
    ILogger<HookDispatcher> logger) : IHookDispatcher
{
    private readonly Dictionary<string, HookTestResult> lastResults = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task DispatchAsync(string workspaceRoot, HookTriggerKind trigger, string payloadJson, CancellationToken cancellationToken)
    {
        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var hooks = (config.Document.Hooks ?? [])
            .Where(hook => hook.Enabled && hook.Trigger == trigger)
            .ToArray();
        if (hooks.Length == 0)
        {
            return;
        }

        foreach (var hook in hooks)
        {
            await ExecuteHookAsync(workspaceRoot, hook, payloadJson, recordResult: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HookStatusRecord>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return (config.Document.Hooks ?? [])
            .OrderBy(static hook => hook.Name, StringComparer.OrdinalIgnoreCase)
            .Select(hook =>
            {
                lastResults.TryGetValue(hook.Name, out var lastResult);
                return new HookStatusRecord(
                    hook.Name,
                    hook.Trigger,
                    hook.Command,
                    hook.Arguments,
                    hook.Enabled,
                    lastResult?.TestedAtUtc,
                    lastResult?.Succeeded,
                    lastResult?.Message);
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<HookTestResult> TestAsync(string workspaceRoot, string hookName, string payloadJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookName);

        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var hook = (config.Document.Hooks ?? [])
            .FirstOrDefault(item => string.Equals(item.Name, hookName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Hook '{hookName}' was not found.");

        return await ExecuteHookAsync(workspaceRoot, hook, payloadJson, recordResult: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HookTestResult> ExecuteHookAsync(
        string workspaceRoot,
        HookDefinition hook,
        string payloadJson,
        bool recordResult,
        CancellationToken cancellationToken)
    {
        try
        {
            await processRunner.RunAsync(
                new ProcessRunRequest(
                    hook.Command,
                    hook.Arguments,
                    workspaceRoot,
                    new Dictionary<string, string?>
                    {
                        ["SHARPCLAW_HOOK_TRIGGER"] = hook.Trigger.ToString(),
                        ["SHARPCLAW_HOOK_PAYLOAD_JSON"] = payloadJson,
                    }),
                cancellationToken).ConfigureAwait(false);

            var success = new HookTestResult(hook.Name, hook.Trigger, true, "Hook executed successfully.", DateTimeOffset.UtcNow);
            if (recordResult)
            {
                lastResults[hook.Name] = success;
            }

            return success;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hook '{HookName}' failed for trigger {Trigger}.", hook.Name, hook.Trigger);
            var failure = new HookTestResult(hook.Name, hook.Trigger, false, ex.Message, DateTimeOffset.UtcNow);
            if (recordResult)
            {
                lastResults[hook.Name] = failure;
            }

            return failure;
        }
    }
}
