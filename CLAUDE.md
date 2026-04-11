# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SharpClaw Code is a C#-native coding-agent runtime (.NET 10 / C# 13) inspired by the Rust `claw-code` surface. It provides durable sessions, permission-aware tool execution, provider abstraction (Anthropic & OpenAI-compatible), MCP integration, and plugin/skill extensibility.

## Build & Test Commands

```bash
# Build
dotnet build SharpClawCode.sln

# Run all tests
dotnet test SharpClawCode.sln

# Run a single test by name
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~YourTestName"

# Run parity harness scenarios only
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~ParityScenarioTests"

# Run the CLI
dotnet run --project src/SharpClaw.Code.Cli -- [arguments]
```

## Architecture

**Entry point:** `src/SharpClaw.Code.Cli/Program.cs` builds a host via `CliHostBuilder`, resolves `CliCommandFactory`, and invokes `System.CommandLine`.

**Core execution flow:**
1. `ConversationRuntime.RunPromptAsync` resolves/creates a session
2. `DefaultTurnRunner` builds prompt context via `IPromptContextAssembler`
3. `PrimaryCodingAgent` delegates to `AgentFrameworkBridge` -> `ProviderBackedAgentKernel` -> `IModelProvider`
4. Tools execute through `IToolRegistry` -> `PermissionPolicyEngine` -> `ISharpClawTool.ExecuteAsync`
5. Events published via `IRuntimeEventPublisher`, sessions persisted as snapshots + NDJSON append logs

**DI composition roots:** `RuntimeServiceCollectionExtensions.AddSharpClawRuntime()` and `CliServiceCollectionExtensions.AddSharpClawCli()`.

**Key project boundaries:**
- `Protocol` is the dependency-light center: DTOs, enums, events, `ProtocolJsonContext` (source-gen JSON)
- `Runtime` owns orchestration and lifecycle, not the CLI
- `Cli` and `Commands` are thin; business logic lives in Runtime/Agents/Tools
- `Tools` must route through `Permissions` -- never bypass permission checks
- `Sessions` supports both durable snapshots and append-only event logs
- `Agents` wraps the Microsoft Agent Framework without leaking provider/transport details

## Engineering Rules (from AGENTS.md)

- **System.Text.Json only** -- never Newtonsoft.Json. Use `ProtocolJsonContext` for source-generated serialization.
- **Async/await end-to-end** for all I/O. Pass `CancellationToken` through async call paths.
- **Constructor injection** over service location.
- **File-scoped namespaces**, records for immutable contracts, interfaces for infrastructure seams.
- **Cross-platform** with Windows-safe path/process handling. No hardcoded secrets or machine-specific paths.
- **XML docs** for all public APIs and major internal services.
- **Permission policy enforcement** for shell execution and file mutation.
- **Typed contracts** over ad hoc dictionaries. Keep JSON outputs stable unless explicitly breaking.
- Prefer incremental vertical slices over broad placeholder scaffolding.

## Test Projects

| Project | Role |
|---------|------|
| `SharpClaw.Code.UnitTests` | Fast unit tests (tools, permissions, serialization, MCP) |
| `SharpClaw.Code.IntegrationTests` | Runtime + provider flows with real composition |
| `SharpClaw.Code.MockProvider` | `DeterministicMockModelProvider` with named scenarios |
| `SharpClaw.Code.ParityHarness` | End-to-end scenarios via `ParityTestHost` with mock LLM |
| `SharpClaw.Code.Mcp.FixtureServer` | MCP fixture server for testing |

Mock provider scenarios: `streaming_text`, `stream_failure`, `stream_slow`, `read_file_roundtrip`, `write_file_allowed`, `write_file_denied`, `grep_chunk_assembly`, `bash_stdout_roundtrip`, `permission_prompt_approved`, `permission_prompt_denied`, `plugin_tool_roundtrip`, `mcp_partial_startup`, `recovery_after_timeout`.

## CLI Modes

- `build` -- normal coding-agent execution (default)
- `plan` -- analysis-first, blocks mutating tools
- `spec` -- generates Kiro-style spec artifacts under `docs/superpowers/specs/`

## Known Backlog (ARCHITECTURE-NOTES.md)

- Commands project directly references Mcp/Plugins (should go through abstractions)
- `McpDoctorService` uses anonymous objects instead of typed DTOs for JSON
- Plugin manifest parsing uses `JsonSerializerDefaults.Web` instead of `ProtocolJsonContext`
- Telemetry sinks not yet wired for production (OpenTelemetry, file traces)
- Runtime directly references Agents (acceptable for now, seam needed if multiple backends appear)
