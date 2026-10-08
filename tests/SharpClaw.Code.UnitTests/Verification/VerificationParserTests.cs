using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Verification;

namespace SharpClaw.Code.UnitTests.Verification;

public sealed class VerificationParserTests
{
    [Fact]
    public void Diagnostics_parse_windows_ranges_spaces_sdk_errors_and_duplicates()
    {
        var output = "C:\\repo name\\File.cs(12,4,12,8): error CS1002: ; expected [C:\\repo name\\App.csproj]\nMSBUILD : error MSB1009: Project file does not exist.\nerror NETSDK1004: Assets file is missing.\n";
        var result = new DotNetDiagnosticParser().Parse(output, output, "C:\\repo name");
        result.Should().HaveCount(3);
        result[0].Path.Should().Be("File.cs");
        result[0].ProjectPath.Should().Be("App.csproj");
        result[0].Line.Should().Be(12);
        result[1].Path.Should().BeNull();
        result[2].Code.Should().Be("NETSDK1004");
    }

    [Fact]
    public void Trx_parses_namespace_and_bounded_failures_and_rejects_entities()
    {
        var parser = new TrxTestResultParser();
        var xml = """<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="Invoice" outcome="Failed"><Output><ErrorInfo><Message>wrong value</Message><StackTrace>at Test()</StackTrace></ErrorInfo></Output></UnitTestResult></Results><ResultSummary><Counters total="1" passed="0" failed="1"/></ResultSummary></TestRun>""";
        var result = parser.Parse(xml, "App.Tests.csproj", "net10.0");
        result.Status.Should().Be(VerificationStatus.Failed);
        result.Cases.Should().ContainSingle().Which.Message.Should().Be("wrong value");
        parser.Parse("<!DOCTYPE TestRun [<!ENTITY x SYSTEM 'file:///secret'>]><TestRun>&x;</TestRun>", "Tests", null).Reason.Should().Be(VerificationFailureReason.TestResultsMissing);
        parser.Parse("<TestRun><Counters total=\"1\" passed=\"1\" failed=\"0\"/></TestRun>", "Tests", null).Reason.Should().Be(VerificationFailureReason.TestResultsMissing);
    }

    [Fact]
    public void Affected_projects_follow_reverse_edges_and_fall_back_for_unknown_items()
    {
        DotNetProjectSummary Project(string path, string[] sources, string[] references) => new(path, path, "C#", ["net10.0"], "net10.0", path.Contains("Tests"), sources.Length, references, [], sources);
        var projects = new[] { Project("Library", ["Library/File.cs"], []), Project("Consumer", ["Consumer/File.cs"], ["Library"]), Project("Tests", ["Tests/Test.cs"], ["Consumer"]), Project("Other", ["Other/File.cs"], []) };
        var solution = new DotNetSolutionSummary(DotNetWorkspaceStatus.Ready, "/workspace", "Fixture.slnx", projects, [new("Consumer", "Library"), new("Tests", "Consumer")], []);
        var resolver = new AffectedProjectResolver();
        resolver.Resolve(solution, ["Library/File.cs"]).Projects.Should().BeEquivalentTo(["Library", "Consumer", "Tests"]);
        resolver.Resolve(solution, ["removed.cs"]).ConservativeFallback.Should().BeTrue();
        resolver.Resolve(solution, ["Directory.Build.props"]).Projects.Should().HaveCount(4);
        resolver.Resolve(solution, []).Projects.Should().HaveCount(4);
    }
}
