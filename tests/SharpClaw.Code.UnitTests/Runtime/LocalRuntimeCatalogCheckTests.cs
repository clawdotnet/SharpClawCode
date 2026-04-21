using FluentAssertions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Runtime.Diagnostics;
using SharpClaw.Code.Runtime.Diagnostics.Checks;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Covers status/doctor reporting for configured local runtime profiles.
/// </summary>
public sealed class LocalRuntimeCatalogCheckTests
{
    [Fact]
    public async Task ExecuteAsync_should_report_profile_health_and_model_counts()
    {
        var check = new LocalRuntimeCatalogCheck(new StubProviderCatalogService());

        var result = await check.ExecuteAsync(
            new OperationalDiagnosticsContext("/workspace", null, PermissionMode.WorkspaceWrite),
            CancellationToken.None);

        result.Status.Should().Be(OperationalCheckStatus.Warn);
        result.Detail.Should().Contain("ollama (Ollama): unhealthy, 1 model(s)");
        result.Detail.Should().Contain("embedding default nomic-embed-text");
    }

    private sealed class StubProviderCatalogService : IProviderCatalogService
    {
        public Task<IReadOnlyList<ProviderModelCatalogEntry>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProviderModelCatalogEntry>>(
            [
                new ProviderModelCatalogEntry(
                    "openai-compatible",
                    "gpt-4.1-mini",
                    [],
                    new AuthStatus(null, false, "openai-compatible", null, null, []),
                    AvailableModels:
                    [
                        new ProviderDiscoveredModel("qwen2.5-coder", "qwen2.5-coder", true, false)
                    ],
                    LocalRuntimeProfiles:
                    [
                        new LocalRuntimeProfileSummary(
                            "ollama",
                            LocalRuntimeKind.Ollama,
                            "http://127.0.0.1:11434/v1/",
                            "qwen2.5-coder",
                            "nomic-embed-text",
                            ProviderAuthMode.Optional,
                            false,
                            "connection refused",
                            [
                                new ProviderDiscoveredModel("qwen2.5-coder", "qwen2.5-coder", true, false)
                            ])
                    ])
            ]);
    }
}
