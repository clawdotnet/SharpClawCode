using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies spec-mode prompt execution and artifact generation.
/// </summary>
public sealed class SpecModeWorkflowTests
{
    /// <summary>
    /// Ensures spec mode writes a full artifact set to the workspace and returns stable metadata.
    /// </summary>
    [Fact]
    public async Task RunPrompt_spec_mode_should_create_artifact_set_and_markdown_files()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
            services.AddSingleton<IModelProviderResolver, SpecModelProviderResolver>();
        });
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "Add a spec mode for feature planning",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "spec-provider",
                    ["model"] = "spec-model"
                },
                PrimaryMode: PrimaryMode.Spec),
            CancellationToken.None);

        result.SpecArtifacts.Should().NotBeNull();
        result.FinalOutput.Should().Contain("Spec generated:");
        File.ReadAllText(result.SpecArtifacts!.RequirementsPath).Should().Contain("When the caller selects spec mode, the system shall generate");
        File.ReadAllText(result.SpecArtifacts.DesignPath).Should().Contain("## Data Flow");
        File.ReadAllText(result.SpecArtifacts.TasksPath).Should().Contain("- [ ] **TASK-001**");
    }

    /// <summary>
    /// Ensures repeated spec prompts create a fresh folder even within the same session.
    /// </summary>
    [Fact]
    public async Task RunPrompt_spec_mode_should_create_new_folder_for_same_session_and_prompt()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
            services.AddSingleton<IModelProviderResolver, SpecModelProviderResolver>();
        });
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var first = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "Add a spec mode for feature planning",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "spec-provider",
                    ["model"] = "spec-model"
                },
                PrimaryMode: PrimaryMode.Spec),
            CancellationToken.None);

        var second = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "Add a spec mode for feature planning",
                SessionId: first.Session.Id,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "spec-provider",
                    ["model"] = "spec-model"
                },
                PrimaryMode: PrimaryMode.Spec),
            CancellationToken.None);

        second.SpecArtifacts!.RootPath.Should().NotBe(first.SpecArtifacts!.RootPath);
        second.SpecArtifacts.RootPath.Should().EndWith("-2");
    }

    /// <summary>
    /// Ensures custom commands can opt into spec mode through frontmatter overrides.
    /// </summary>
    [Fact]
    public async Task ExecuteCustomCommand_with_spec_mode_override_should_generate_spec_artifacts()
    {
        var workspacePath = CreateTemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspacePath, ".sharpclaw", "commands"));
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, ".sharpclaw", "commands", "feature-spec.md"),
            """
            ---
            description: Generate a feature spec
            primaryMode: spec
            ---
            Create a full spec for $ARGUMENTS
            """);

        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
            services.AddSingleton<IModelProviderResolver, SpecModelProviderResolver>();
        });
        var runtime = serviceProvider.GetRequiredService<IRuntimeCommandService>();

        var result = await runtime.ExecuteCustomCommandAsync(
            "feature-spec",
            "spec mode tasking",
            new RuntimeCommandContext(
                WorkingDirectory: workspacePath,
                Model: "spec-model",
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                PrimaryMode: PrimaryMode.Build,
                SessionId: null),
            CancellationToken.None);

        result.SpecArtifacts.Should().NotBeNull();
        File.Exists(result.SpecArtifacts!.RequirementsPath).Should().BeTrue();
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-spec-mode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private static ServiceProvider CreateRuntimeServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        configure(services);
        return services.BuildServiceProvider();
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("spec-subject", true, providerName, null, null, ["spec"]));
    }

    private sealed class SpecModelProviderResolver : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => new SpecModelProvider();
    }

    private sealed class SpecModelProvider : IModelProvider
    {
        public string ProviderName => "spec-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("spec-subject", true, ProviderName, null, null, ["spec"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request)));

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(ProviderRequest request)
        {
            yield return new ProviderEvent(
                "spec-event-1",
                request.Id,
                "delta",
                DateTimeOffset.Parse("2026-04-08T00:00:00Z"),
                """
                {
                  "requirements": {
                    "title": "Spec Mode Requirements",
                    "summary": "Add a spec-writing workflow to the runtime.",
                    "requirements": [
                      {
                        "id": "REQ-001",
                        "statement": "When the caller selects spec mode, the system shall generate requirements, design, and tasks documents.",
                        "rationale": "The workflow must produce consistent planning artifacts."
                      }
                    ]
                  },
                  "design": {
                    "title": "Spec Mode Design",
                    "summary": "Append a structured prompt contract and render parsed JSON into markdown files.",
                    "architecture": ["Keep spec mode in the existing prompt pipeline rather than introducing a separate command engine."],
                    "dataFlow": ["Prompt assembly appends the spec contract, then the runtime parses the model output and writes files."],
                    "interfaces": ["Extend PrimaryMode and TurnExecutionResult with spec-mode metadata."],
                    "failureModes": ["Reject invalid JSON and clean up partial spec folders."],
                    "testing": ["Cover parsing, folder naming, command surfaces, and runtime generation flows."]
                  },
                  "tasks": {
                    "title": "Spec Mode Tasks",
                    "tasks": [
                      {
                        "id": "TASK-001",
                        "description": "Implement the spec workflow service and wire it into prompt execution.",
                        "doneCriteria": "Spec prompts generate three markdown files with stable metadata."
                      }
                    ]
                  }
                }
                """,
                false,
                null);
            await Task.Yield();
            yield return new ProviderEvent(
                "spec-event-2",
                request.Id,
                "completed",
                DateTimeOffset.Parse("2026-04-08T00:00:01Z"),
                null,
                true,
                new UsageSnapshot(10, 20, 0, 30, null));
        }
    }
}
