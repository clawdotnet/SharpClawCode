using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Parses portable .NET build output once for all diagnostic consumers.</summary>
public interface IDotNetDiagnosticParser
{
    /// <summary>Parses and deduplicates stdout/stderr diagnostics, including Windows paths and location-free SDK failures.</summary>
    VerificationDiagnostic[] Parse(string standardOutput, string standardError, string workspaceRoot);
}
