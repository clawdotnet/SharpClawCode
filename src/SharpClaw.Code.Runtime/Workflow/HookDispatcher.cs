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
            try
            {
                await processRunner.RunAsync(
                    new ProcessRunRequest(
                        hook.Command,
                        hook.Arguments,
                        workspaceRoot,
                        new Dictionary<string, string?>
                        {
                            ["SHARPCLAW_HOOK_TRIGGER"] = trigger.ToString(),
                            ["SHARPCLAW_HOOK_PAYLOAD_JSON"] = payloadJson,
                        }),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Hook '{HookName}' failed for trigger {Trigger}.", hook.Name, trigger);
            }
        }
    }
}
