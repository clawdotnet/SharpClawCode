# Architecture

This document matches the **current** solution: `SharpClawCode.sln` with projects under `src/` and test projects under `tests/`.

## Solution structure

| Project | Role |
|---------|------|
| **SharpClaw.Code.Protocol** | DTOs, enums, events, command results, JSON source context (`ProtocolJsonContext`). No NuGet dependencies. |
| **SharpClaw.Code.Infrastructure** | File system and path abstractions, shared helpers referenced by Sessions and others. |
| **SharpClaw.Code.Telemetry** | `IRuntimeEventPublisher`, ring buffer, optional `IRuntimeEventPersistence`; usage tracking. |
| **SharpClaw.Code.Permissions** | `IPermissionPolicyEngine`, rules, `IApprovalService`, session approval memory. |
| **SharpClaw.Code.Providers** | `IModelProvider`, `AnthropicProvider`, `OpenAiCompatibleProvider`, resolver, auth preflight. |
| **SharpClaw.Code.Tools** | `IToolRegistry`, `IToolExecutor`, built-in tools, plugin tool proxies. |
| **SharpClaw.Code.Sessions** | `ISessionStore`, `IEventStore`, file-backed snapshot + NDJSON append log. |
| **SharpClaw.Code.Mcp** | `IMcpRegistry`, `IMcpServerHost`, `IMcpDoctorService`, file-backed registry; **`SdkMcpProcessSupervisor`** uses the official **ModelContextProtocol** client for stdio, HTTP (auto/streamable), and SSE transports. |
| **SharpClaw.Code.Plugins** | `IPluginManager`, manifest install, out-of-process loader. |
| **SharpClaw.Code.Acp** | ACP stdio host surface for editor and protocol bridge scenarios. |
| **SharpClaw.Code.Memory / Git / Web / Skills** | Context and auxiliary services composed by runtime/agents as implemented today. |
| **SharpClaw.Code.Agents** | Microsoft Agent Framework bridge, `ProviderBackedAgentKernel`, concrete agents. |
| **SharpClaw.Code.Runtime** | `ConversationRuntime`, `DefaultTurnRunner`, lifecycle/state machine, operational diagnostics DI. |
| **SharpClaw.Code** | Embeddable runtime SDK: `SharpClawRuntimeHostBuilder`, `SharpClawRuntimeHost`, host-aware runtime entrypoints. |
| **SharpClaw.Code.Commands** | System.CommandLine handlers, REPL host, slash commands, output renderers dispatch. |
| **SharpClaw.Code.Cli** | Entry point (`Program.cs`), `Host` wiring: `AddSharpClawRuntime` + `AddSharpClawCli`. |

Test projects: **UnitTests**, **IntegrationTests**, **MockProvider**, **ParityHarness**, **Mcp.FixtureServer**. Example hosts are included in the solution under `examples/`.

## Composition overview

1. **`CliHostBuilder.BuildHost`** builds `Host.CreateApplicationBuilder`, registers **Runtime** then **CLI**.
2. **`SharpClawRuntimeHostBuilder`** builds the same runtime slice without CLI assumptions for embedded ASP.NET Core, worker-service, or SDK hosts.
3. **`RuntimeServiceCollectionExtensions.AddSharpClawRuntime`** (with `IConfiguration` when used from CLI) registers in order: Telemetry, Infrastructure, **Providers** (from config), **Mcp**, **Tools**, **Agents**, Memory, Skills, Git, **Sessions** stores, context assembler, **DefaultTurnRunner**, state machine, **operational diagnostics checks**, **`ConversationRuntime`** (as `IConversationRuntime` + `IRuntimeCommandService`), and a minimal **`IHostedService`** (`RuntimeCoordinatorHostedServiceAdapter`).
4. **`AddSharpClawCli`** registers command handlers, REPL, renderers, `CommandRegistry`.

## Major execution flows

### Prompt (CLI `prompt` or REPL input line)

1. **`IRuntimeCommandService.ExecutePromptAsync`** → **`ConversationRuntime.RunPromptAsync`**.
2. Resolves or creates **`ConversationSession`** under the workspace; transitions lifecycle; appends **`RuntimeEvent`**s when persistence is enabled.
3. **`DefaultTurnRunner.RunAsync`** builds prompt context (`IPromptContextAssembler`), constructs **`AgentRunContext`** (includes **`IToolExecutor`** and normalized host/tenant context), calls **`PrimaryCodingAgent.RunAsync`**.
4. **`SharpClawAgentBase`** delegates to **`AgentFrameworkBridge.RunAsync`**, which drives **`ProviderBackedAgentKernel`** (streaming `IModelProvider`, auth checks, **`ProviderExecutionException`** on hard failures).
5. Turn completion updates session, checkpoints as implemented in **`ConversationRuntime`**, publishes events via **`IRuntimeEventPublisher`**.

**Note:** `AgentRunContext` carries **`IToolExecutor`**, and the current **`AgentFrameworkBridge`** path advertises the resolved tool set to the provider, executes tool calls through the permission-aware executor, and records tool results in the agent run result. Prompt references and tool approvals respect the caller's normalized interactivity mode.

### Operational commands

- **`doctor` / `status` / `session show`** use **`IOperationalDiagnosticsCoordinator`** and **`IRuntimeCommandService`** to assemble **Protocol** reports and render via **`OutputRendererDispatcher`**.

### Tool execution (when invoked)

1. **`ToolExecutor.ExecuteAsync`** → **`IToolRegistry.GetRequiredAsync`** (built-ins + enabled plugin tools).
2. **`PermissionPolicyEngine.EvaluateAsync`** runs ordered **`IPermissionRule`** implementations; may **`IApprovalService`**.
3. On allow, **`ISharpClawTool.ExecuteAsync`**; tool/runtime events may flow to **`IRuntimeEventPublisher`** with session persistence options.

## Configuration

Providers bind to **`IConfiguration`** sections (see `docs/providers.md`). The CLI host uses `Host.CreateApplicationBuilder(args)`, so standard **`appsettings.json`** / environment variables apply when present in the application content root.

## Further reading

- Runtime details: [runtime.md](runtime.md)  
- Sessions layout: [sessions.md](sessions.md)  
- Backlog notes: [../ARCHITECTURE-NOTES.md](../ARCHITECTURE-NOTES.md)
