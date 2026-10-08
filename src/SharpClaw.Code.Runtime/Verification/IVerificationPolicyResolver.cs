using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Resolves validated request metadata over merged workspace/user verification options.</summary>
public interface IVerificationPolicyResolver
{
    /// <summary>Reads policy without executing projects.</summary>
    Task<VerificationPolicy> ResolveAsync(string workspaceRoot, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken);
}
