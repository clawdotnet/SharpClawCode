using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Supplies only diagnostics from previously authorized verification; reading never executes project code.</summary>
public interface IVerificationDiagnosticsCache
{
    /// <summary>Stores the latest report containing a build, with bounded workspace retention.</summary>
    void Store(VerificationRunReport report);
    /// <summary>Gets a recent authorized build report, if one exists.</summary>
    VerificationRunReport? Get(string workspaceRoot);
}
