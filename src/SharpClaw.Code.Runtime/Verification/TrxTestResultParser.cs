using System.Xml;
using System.Xml.Linq;
using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Parses namespace-tolerant TRX results with bounded failure details.</summary>
public sealed class TrxTestResultParser : ITestResultParser
{
    /// <inheritdoc />
    public VerificationTestRunReport Parse(string xml, string projectPath, string? targetFramework)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
            var document = XDocument.Load(reader);
            var counters = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Counters");
            if (counters is null) throw new XmlException("TRX counters are missing.");
            int Count(string name) => int.TryParse(counters.Attribute(name)?.Value, out var number) && number >= 0 ? number : throw new XmlException("Invalid TRX counter: " + name);
            var total = Count("total");
            var passed = Count("passed");
            var failed = Count("failed");
            var skipped = total - passed - failed;
            var results = document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
            if (skipped < 0 || results.Length < total) throw new XmlException("TRX results are incomplete.");
            var cases = results.OrderBy(element => string.Equals(element.Attribute("outcome")?.Value, "Failed", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(500).Select(element => new VerificationTestCaseResult(projectPath, targetFramework, element.Attribute("testName")?.Value ?? "(unnamed)", element.Attribute("outcome")?.Value ?? "Unknown", Text(element, "Message"), Text(element, "StackTrace"))).ToArray();
            var nonPassing = results.Any(element => element.Attribute("outcome")?.Value is not ("Passed" or "NotExecuted" or "Inconclusive"));
            var status = failed > 0 || nonPassing || total == 0 ? VerificationStatus.Failed : VerificationStatus.Passed;
            return new(status, status == VerificationStatus.Passed ? VerificationFailureReason.None : VerificationFailureReason.TestFailed, total, passed, failed, skipped, cases, [], []);
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException)
        {
            return new(VerificationStatus.Failed, VerificationFailureReason.TestResultsMissing, 0, 0, 0, 0, [], [], [error.Message]);
        }
    }
    private static string? Text(XElement element, string name) => element.Descendants().FirstOrDefault(item => item.Name.LocalName == name)?.Value is { } text ? DotNetDiagnosticParser.Bound(text, 4000) : null;
}
