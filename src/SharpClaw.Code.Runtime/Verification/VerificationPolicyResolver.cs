using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Applies explicit metadata overrides and a hard repair ceiling of five.</summary>
public sealed class VerificationPolicyResolver(ISharpClawConfigService configuration) : IVerificationPolicyResolver
{
    /// <inheritdoc />
    public async Task<VerificationPolicy> ResolveAsync(string workspaceRoot, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        var options = (await configuration.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false)).Document.Verification ?? new();
        var enabled = Boolean(SharpClawWorkflowMetadataKeys.VerificationEnabled, options.Enabled ?? false);
        var restore = Boolean(SharpClawWorkflowMetadataKeys.VerificationAllowRestore, options.AllowRestore ?? false);
        var scope = options.Scope ?? VerificationScope.Affected;
        var repairs = options.MaxRepairIterations ?? 2;
        if (metadata?.TryGetValue(SharpClawWorkflowMetadataKeys.VerificationScope, out var scopeText) == true && !Enum.TryParse(scopeText, true, out scope)) throw new ArgumentException("Invalid verification scope.");
        if (metadata?.TryGetValue(SharpClawWorkflowMetadataKeys.VerificationMaxRepairIterations, out var repairText) == true && !int.TryParse(repairText, out repairs)) throw new ArgumentException("Invalid verification repair budget.");
        if (!Enum.IsDefined(scope) || repairs is < 0 or > 5) throw new ArgumentException("Verification repair budget must be 0–5 and scope must be supported.");
        return new(enabled, scope, options.RunAfterMutatingTurn ?? true, repairs, restore);
        bool Boolean(string key, bool fallback) => metadata?.TryGetValue(key, out var value) == true ? bool.TryParse(value, out var parsed) ? parsed : throw new ArgumentException("Invalid boolean metadata: " + key) : fallback;
    }
}
