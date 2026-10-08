using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Reads test reports without resolving DTDs or external entities.</summary>
public interface ITestResultParser
{
    /// <summary>Parses one completed TRX report and associates its cases with a project/framework.</summary>
    VerificationTestRunReport Parse(string xml, string projectPath, string? targetFramework);
}
