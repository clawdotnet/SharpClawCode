using System.Text.Json;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Builds bounded repair input from structured failures rather than raw process logs.</summary>
public static class VerificationFailureContextFormatter
{
    /// <summary>Formats the current request, prior response, failed diagnostics and test cases.</summary>
    public static string Format(string originalPrompt, string previousOutput, VerificationRunReport report, int attempt, int limit)
    {
        var failures = new VerificationRepairContext(report.Status, report.Reason, report.Scope, report.Target, report.AffectedProjects,
            (report.Build?.Diagnostics ?? []).Where(item => item.Severity == "error").Take(30).ToArray(),
            (report.Tests?.Cases ?? []).Where(item => item.Outcome != "Passed").Take(10).ToArray(), report.Summary);
        return $"Repair attempt {attempt} of {limit}. Fix the structured verification failures using the existing tools and permissions. Preserve the requested behavior.\n\nOriginal request:\n{DotNetDiagnosticParser.Bound(originalPrompt, 6000)}\n\nPrevious response:\n{DotNetDiagnosticParser.Bound(previousOutput, 3000)}\n\nVerification failures:\n{DotNetDiagnosticParser.Bound(JsonSerializer.Serialize(failures, ProtocolJsonContext.Default.VerificationRepairContext), 8000)}";
    }
}
