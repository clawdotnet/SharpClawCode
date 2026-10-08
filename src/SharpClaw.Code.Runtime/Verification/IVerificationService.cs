using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Executes verification for a trusted host. Untrusted callers must use the verify_workspace tool permission boundary.</summary>
public interface IVerificationService
{
    /// <summary>Verifies a contained workspace. This executes project code and requires prior host authorization.</summary>
    Task<VerificationRunReport> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken);
}
