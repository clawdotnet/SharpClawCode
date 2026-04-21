# Testing

## Layout

| Project | Purpose |
|---------|---------|
| **SharpClaw.Code.UnitTests** | Fast tests (tools, permissions, serialization, MCP/plugin units, …) |
| **SharpClaw.Code.IntegrationTests** | Runtime + provider flows with real composition |
| **SharpClaw.Code.MockProvider** | **`DeterministicMockModelProvider`**, **`AddDeterministicMockModelProvider`**, **`ParityMetadataKeys`**, **`ParityProviderScenario`** |
| **SharpClaw.Code.ParityHarness** | End-to-end scenarios over real **`AddSharpClawRuntime`** + mock LLM |

Run all tests:

```bash
dotnet test SharpClawCode.sln
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
- **`stream_slow`** — delays (cancellation / timeout scenarios)

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

**Note:** Many scenarios exercise **`IToolExecutor`** directly rather than going through the LLM agent loop (which matches current **`AgentFrameworkBridge`** behavior).

## CI

CI restores and builds the full solution, explicitly builds every example host project, and then runs `dotnet test` on the solution. Parity tests use temp directories under **`Path.GetTempPath()`** and avoid network.
