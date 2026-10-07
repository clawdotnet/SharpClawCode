# Testing

## Layout

| Project | Purpose |
|---------|---------|
| **SharpClaw.Code.UnitTests** | Fast tests (tools, permissions, serialization, MCP/plugin units, …) |
| **SharpClaw.Code.IntegrationTests** | Runtime + provider flows with real composition |
| **SharpClaw.Code.MockProvider** | **`DeterministicMockModelProvider`**, **`AddDeterministicMockModelProvider`**, **`ParityMetadataKeys`**, **`ParityProviderScenario`** |
| **SharpClaw.Code.ParityHarness** | End-to-end scenarios over real **`AddSharpClawRuntime`** + mock LLM |
| **SharpClaw.Testing.\*** | JSON scenario contracts, oracle runner, CLI commands, and xUnit adapter for explicit agent testing |

Run all tests:

```bash
dotnet test SharpClawCode.sln
```

Run the explicit agent scenario harness:

```bash
dotnet run --project src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj -- test run
dotnet run --project src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj -- test gates
```

Build the example hosts as part of normal validation:

```bash
dotnet build examples/WebApiAgent/WebApiAgent.csproj
dotnet build examples/MinimalConsoleAgent/MinimalConsoleAgent.csproj
dotnet build examples/WorkerServiceHost/WorkerServiceHost.csproj
dotnet build examples/McpToolAgent/McpToolAgent.csproj
```

Filter examples:

```bash
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~ParityScenarioTests"
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~ToolRegistry"
```

## Mock provider

**`DeterministicMockModelProvider`** implements **`IModelProvider`** with provider name **`mock`**.

Scenarios are selected via request **`Metadata`** key **`parityScenario`** (**`ParityMetadataKeys.Scenario`**):

- **`streaming_text`** — deterministic deltas → `"Hello world"`
- **`stream_failure`** — throws (turn fails; session may become **`Failed`**)
- **`stream_slow`** — emits an initial delta, then waits for cancellation (timeout / recovery scenarios)

**`AddDeterministicMockModelProvider`** registers the provider + **`PostConfigure<ProviderCatalogOptions>`** so **`default`** / **`deterministic`** aliases point at the mock.

## Parity harness

**`ParityTestHost.Create`** (`tests/SharpClaw.Code.ParityHarness/ParityTestHost.cs`):

1. Empty **`IConfiguration`**
2. **`AddSharpClawRuntime(configuration)`**
3. **`AddDeterministicMockModelProvider()`**
4. Optional **`ParityFixturePluginTool`** as **`ISharpClawTool`**
5. Optional **`ReplaceWithScriptedApprovals(bool)`** — swaps **`IApprovalService`** for deterministic approve/deny

**`ParityScenarioTests`** cover Provider/runtime, **`IToolExecutor`** (read/write/grep/bash), permissions, plugin echo tool, MCP registry partial startup, recovery after timeout.

Stable scenario **ids** are listed in **`ParityScenarioIds`** (e.g. `streaming_text`, `read_file_roundtrip`, `write_file_allowed`, `write_file_denied`, `grep_chunk_assembly`, `bash_stdout_roundtrip`, `permission_prompt_approved`, `permission_prompt_denied`, `plugin_tool_roundtrip`, `mcp_partial_startup`, `recovery_after_timeout`).

The parity suite covers both direct **`IToolExecutor`** boundaries and full provider-backed tool-loop behavior. Direct tests isolate permission and filesystem behavior; integration tests verify that provider tool requests traverse the same executor and return results to the model.

## Agent scenario harness

The scenario harness lives in **`SharpClaw.Testing.Abstractions`**, **`SharpClaw.Testing.Harness`**, **`SharpClaw.Testing.Cli`**, and **`SharpClaw.Testing.Xunit`**. Scenario files live in **`tests/agent-scenarios`** and use JSON with explicit oracles. See **`docs/testing/agent-testing-harness.md`** for the contract, CLI usage, xUnit adapter, and gate model.

## CI

CI restores and builds the full solution with warnings treated as errors, explicitly builds every example host, runs `dotnet test`, then runs the scenario harness through `sharpclaw test run` and `sharpclaw test gates`. It also enforces the line-coverage floor, compiles the VS Code extension, audits dependencies, and smoke-installs production packages on Linux, Windows, and macOS. Parity tests use temp directories under **`Path.GetTempPath()`** and avoid network.
