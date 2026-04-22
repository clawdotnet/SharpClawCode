using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies instruction rule loading respects document and total-size budgets.
/// </summary>
public sealed class InstructionRuleServiceTests
{
    [Fact]
    public async Task LoadAsync_should_not_exceed_total_budget_when_remaining_space_is_tiny()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var globalRulesPath = Path.Combine(workspacePath, ".sharpclaw-home", "rules");
        Directory.CreateDirectory(globalRulesPath);

        for (var index = 0; index < 11; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(globalRulesPath, $"rule-{index:D2}.md"),
                new string((char)('a' + index), 1_090),
                CancellationToken.None);
        }

        await File.WriteAllTextAsync(
            Path.Combine(globalRulesPath, "rule-11.md"),
            new string('z', 200),
            CancellationToken.None);

        var service = new InstructionRuleService(
            new LocalFileSystem(),
            new PathService(),
            new FixedUserProfilePaths(workspacePath));

        var snapshot = await service.LoadAsync(workspacePath, CancellationToken.None);

        snapshot.Documents.Should().HaveCount(12);
        snapshot.Documents.Sum(static document => document.Content.Length).Should().BeLessThanOrEqualTo(12_000);
        snapshot.Documents[^1].Content.Length.Should().BeLessThanOrEqualTo(10);
        snapshot.Documents[^1].IsTruncated.Should().BeTrue();
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-rule-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private sealed class FixedUserProfilePaths(string root) : IUserProfilePaths
    {
        public string GetUserCustomCommandsDirectory()
            => Path.Combine(root, ".sharpclaw-home", "commands");

        public string GetUserHomeDirectory()
            => root;

        public string GetUserSharpClawRoot()
            => Path.Combine(root, ".sharpclaw-home");
    }
}
