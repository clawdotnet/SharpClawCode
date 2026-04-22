using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.UnitTests.Protocol;

/// <summary>
/// Verifies the generated JSON protocol metadata and representative payloads.
/// </summary>
public sealed class ProtocolJsonContextTests
{
    /// <summary>
    /// Ensures provider requests serialize with camelCase names and string enums.
    /// </summary>
    [Fact]
    public void Provider_request_should_serialize_with_protocol_json_context()
    {
        var request = new ProviderRequest(
            Id: "provider-request-001",
            SessionId: "session-001",
            TurnId: "turn-001",
            ProviderName: "openai",
            Model: "gpt-5.4",
            Prompt: "Inspect the workspace.",
            SystemPrompt: "Be concise.",
            OutputFormat: OutputFormat.Json,
            Temperature: 0.2m,
            Metadata: new Dictionary<string, string>
            {
                ["channel"] = "cli"
            });

        var json = JsonSerializer.Serialize(request, ProtocolJsonContext.Default.ProviderRequest);

        json.Should().Contain("\"providerName\":\"openai\"");
        json.Should().Contain("\"outputFormat\":\"json\"");
        json.Should().Contain("\"metadata\":{\"channel\":\"cli\"}");
    }

    /// <summary>
    /// Ensures runtime events round-trip through the protocol polymorphic contract.
    /// </summary>
    [Fact]
    public void Runtime_events_should_round_trip_polymorphically()
    {
        RuntimeEvent runtimeEvent = new ToolCompletedEvent(
            EventId: "event-001",
            SessionId: "session-001",
            TurnId: "turn-001",
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-05T22:00:00Z"),
            Result: new ToolResult(
                RequestId: "tool-request-001",
                ToolName: "Shell",
                Succeeded: true,
                OutputFormat: OutputFormat.Json,
                Output: "{\"status\":\"ok\"}",
                ErrorMessage: null,
                ExitCode: 0,
                DurationMilliseconds: 42,
                StructuredOutputJson: "{\"status\":\"ok\"}"));

        var json = JsonSerializer.Serialize(runtimeEvent, ProtocolJsonContext.Default.RuntimeEvent);
        var deserialized = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.RuntimeEvent);

        deserialized.Should().BeOfType<ToolCompletedEvent>();
        json.Should().Contain("\"$eventType\":\"toolCompleted\"");
    }

    /// <summary>
    /// Ensures spec artifact metadata serializes through the shared protocol context.
    /// </summary>
    [Fact]
    public void Turn_execution_result_should_serialize_spec_artifacts()
    {
        var result = new TurnExecutionResult(
            Session: new ConversationSession(
                Id: "session-001",
                Title: "Spec session",
                State: SessionLifecycleState.Active,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Json,
                WorkingDirectory: "/workspace",
                RepositoryRoot: "/workspace",
                CreatedAtUtc: DateTimeOffset.Parse("2026-04-08T00:00:00Z"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-04-08T00:05:00Z"),
                ActiveTurnId: null,
                LastCheckpointId: null,
                Metadata: []),
            Turn: new ConversationTurn(
                Id: "turn-001",
                SessionId: "session-001",
                SequenceNumber: 1,
                Input: "spec this",
                Output: "Spec generated",
                StartedAtUtc: DateTimeOffset.Parse("2026-04-08T00:00:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-04-08T00:05:00Z"),
                AgentId: "primary-coding-agent",
                SlashCommandName: null,
                Usage: null,
                Metadata: []),
            FinalOutput: "Spec generated",
            ToolResults: [],
            Usage: null,
            Checkpoint: null,
            Events: [],
            SpecArtifacts: new SpecArtifactSet(
                Slug: "my-spec",
                RootPath: "/workspace/docs/superpowers/specs/2026-04-08-my-spec",
                RequirementsPath: "/workspace/docs/superpowers/specs/2026-04-08-my-spec/requirements.md",
                DesignPath: "/workspace/docs/superpowers/specs/2026-04-08-my-spec/design.md",
                TasksPath: "/workspace/docs/superpowers/specs/2026-04-08-my-spec/tasks.md",
                GeneratedAtUtc: DateTimeOffset.Parse("2026-04-08T00:05:00Z")));

        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.TurnExecutionResult);

        json.Should().Contain("\"specArtifacts\":");
        json.Should().Contain("\"slug\":\"my-spec\"");
    }

    /// <summary>
    /// Ensures plan-mode execution metadata serializes through the shared protocol context.
    /// </summary>
    [Fact]
    public void Turn_execution_result_should_serialize_plan_result()
    {
        var result = new TurnExecutionResult(
            Session: new ConversationSession(
                Id: "session-001",
                Title: "Plan session",
                State: SessionLifecycleState.Active,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Json,
                WorkingDirectory: "/workspace",
                RepositoryRoot: "/workspace",
                CreatedAtUtc: DateTimeOffset.Parse("2026-04-21T00:00:00Z"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-04-21T00:05:00Z"),
                ActiveTurnId: null,
                LastCheckpointId: null,
                Metadata: []),
            Turn: new ConversationTurn(
                Id: "turn-001",
                SessionId: "session-001",
                SequenceNumber: 1,
                Input: "plan this",
                Output: "Deep plan ready.",
                StartedAtUtc: DateTimeOffset.Parse("2026-04-21T00:00:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-04-21T00:05:00Z"),
                AgentId: "primary-coding-agent",
                SlashCommandName: null,
                Usage: null,
                Metadata: []),
            FinalOutput: "Deep plan ready.",
            ToolResults: [],
            Usage: null,
            Checkpoint: null,
            Events: [],
            PlanResult: new PlanExecutionResult(
                Summary: "Audit current seams before editing.",
                Assumptions: ["Git is available."],
                Risks: ["Branch naming collisions."],
                NextAction: "Inspect the git service.",
                Tasks:
                [
                    new PlanTaskItem("PLAN-001", "Inspect the git service", TodoStatus.Open, null, null)
                ],
                TodoSync: new ManagedTodoSyncResult(
                    OwnerAgentId: "deep-planning",
                    AddedCount: 1,
                    UpdatedCount: 0,
                    RemovedCount: 0,
                    ActiveTodos:
                    [
                        new TodoItem("todo-001", "PLAN-001: Inspect the git service", TodoStatus.Open, TodoScope.Session, DateTimeOffset.Parse("2026-04-21T00:04:00Z"), DateTimeOffset.Parse("2026-04-21T00:04:00Z"), "deep-planning", "session-001")
                    ])));

        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.TurnExecutionResult);

        json.Should().Contain("\"planResult\":");
        json.Should().Contain("\"nextAction\":\"Inspect the git service.\"");
        json.Should().Contain("\"ownerAgentId\":\"deep-planning\"");
    }
}
