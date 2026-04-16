# Runtime

The **runtime** layer is centered on **`SharpClaw.Code.Runtime`** and especially **`ConversationRuntime`**, which implements:

- Session surface on **`IConversationRuntime`** — create/get session, **`RunPromptAsync`**
- **`IRuntimeCommandService`** — prompt execution plus status, doctor, session inspection, share/unshare, and compaction commands

Registration: `RuntimeServiceCollectionExtensions.AddSharpClawRuntime`.

## Turn execution

**`DefaultTurnRunner`** is the **`ITurnRunner`** implementation used for prompt turns. It:

1. Calls **`IPromptContextAssembler.AssembleAsync`** to build **`PromptContext`** (prompt text, metadata such as resolved **`model`**, and prior-turn conversation history assembled from persisted runtime events).
2. Maps **`RunPromptRequest`** + session into **`AgentRunContext`** (session/turn ids, working directory, permission mode, output format, **`IToolExecutor`**, metadata, and normalized caller interactivity).
3. Invokes **`PrimaryCodingAgent.RunAsync`**.

Before the agent runs, **`ConversationRuntime`** also layers in:

- merged SharpClaw JSONC config (`ISharpClawConfigService`)
- resolved agent defaults (`IAgentCatalogService`)
- persisted active-agent metadata from the session, when present
- auto-share policy checks (`ShareMode.Auto`)

The agent stack is described in [agents.md](agents.md).

## Agent tool loop

The current runtime supports provider-driven tool calling through the normal agent path.

- **`AgentFrameworkBridge`** resolves the effective allowed-tool set from agent metadata.
- It advertises only that filtered tool set to the provider.
- **`ProviderBackedAgentKernel`** runs the provider loop, dispatches tool-use events through **`IToolExecutor`**, and feeds tool results back into the next provider iteration.
- Tool execution still goes through the normal permission engine, so allowlists, approval requirements, plugin trust, MCP trust, and primary-mode mutation restrictions all apply consistently.

This means the model-visible tool surface and the executor-visible tool surface are derived from the same resolved policy input.

## Lifecycle and state

- **`IRuntimeStateMachine`** (`DefaultRuntimeStateMachine`) transitions **`ConversationSession.State`**.
- Failures (including **`ProviderExecutionException`** and cancellation paths) update session state and append **`SessionStateChangedEvent`** (and related events) when **`AppendEventAsync`** runs with persistence enabled.

## Context assembly

**`PromptContextAssembler`** pulls workspace/session-aware data (skills registry, todo state, durable project/user memory, workspace index status, and git context) into the prompt path before the agent runs.

It also includes a compact diagnostics summary from **`IWorkspaceDiagnosticsService`**, which currently surfaces configured diagnostics sources and build-derived findings for .NET workspaces.

Prompt references are resolved before provider execution. Outside-workspace file references are evaluated through the permission engine, and the approval path now respects the normalized caller interactivity mode:

- interactive CLI and REPL callers can participate in approval prompts
- non-interactive surfaces such as ACP and the embedded HTTP server cannot

When the effective **`PrimaryMode`** is **`Spec`**, the assembler appends a structured output contract that requires the model to return machine-readable requirements, design, and task content.

Conversation history is rebuilt from persisted session events and truncated by token budget before being attached to the next provider request. Assistant history prefers the persisted final turn output and only falls back to streamed provider deltas when needed.

Cross-session memory is sourced from:

- manual project instructions such as `SHARPCLAW.md`
- structured project memory stored under `.sharpclaw/knowledge/knowledge.db`
- structured user memory stored under the SharpClaw user root

The runtime injects only compact recall text and index freshness metadata. Detailed retrieval happens through explicit workspace-search tools and ACP/CLI search calls.

## Spec workflow

**`ISpecWorkflowService`** handles the post-processing path for **`spec`** mode:

- parses the model response as structured JSON
- derives a dated slug from the original prompt
- writes:
  - `requirements.md`
  - `design.md`
  - `tasks.md`
- places them under `docs/superpowers/specs/<yyyy-MM-dd>-<slug>/`

Each spec-mode prompt creates a fresh folder. If the same slug already exists, the runtime appends `-2`, `-3`, and so on instead of overwriting an existing spec set.

## Operational diagnostics

**`OperationalDiagnosticsCoordinator`** runs injectable **`IOperationalCheck`** implementations:

- Workspace, configuration, session store, shell, git, provider auth, local runtime catalog health, MCP registry/host, plugin registry.

Used by **`GetStatusAsync`**, **`RunDoctorAsync`**, and **`InspectSessionAsync`** to build **Protocol** reports (`DoctorReport`, `RuntimeStatusReport`, `SessionInspectionReport`).

`RuntimeStatusReport` now also carries:

- configured diagnostics source count
- current diagnostic error count
- current diagnostic warning count

## Config, agents, and hooks

The parity layer adds several runtime-owned services:

- **`ISharpClawConfigService`** — loads user/workspace `config.jsonc` + `sharpclaw.jsonc` and merges them by precedence
- **`IAgentCatalogService`** — overlays configured specialist agents on top of built-in agents
- **`IConversationCompactionService`** — creates durable session summaries stored in session metadata
- compaction also promotes a project-scoped summary memory entry for later session recall
- **`IShareSessionService`** — creates and removes self-hosted share snapshots
- **`IHookDispatcher`** — executes configured hook processes for turn/tool/share/server events and exposes hook inspection/testing
- **`ITodoService`** — persists session and workspace todo items under session metadata and `.sharpclaw/tasks.json`
- **`IWorkspaceInsightsService`** — reconstructs durable usage, cost, and execution stats from persisted event logs

These services are intentionally small and runtime-owned rather than separate orchestration subsystems.

## Embedded server

**`IWorkspaceHttpServer`** / **`WorkspaceHttpServer`** expose a minimal local HTTP surface for editor and automation clients:

- `POST /v1/prompt`
- `GET /v1/sessions`
- `GET /v1/sessions/{id}`
- `POST /v1/share/{sessionId}`
- `DELETE /v1/share/{sessionId}`
- `GET /v1/status`
- `GET /v1/doctor`
- `GET /s/{shareId}`

Prompt requests can return JSON or replay the completed runtime event stream as SSE.

## Hosted service

**`RuntimeCoordinatorHostedServiceAdapter`** is registered as **`IHostedService`** and currently logs start/stop only (placeholder for future lifecycle coordination).

## Key types (file locations)

| Type | Project / path |
|------|----------------|
| `ConversationRuntime` | `src/SharpClaw.Code.Runtime/Orchestration/ConversationRuntime.cs` |
| `DefaultTurnRunner` | `src/SharpClaw.Code.Runtime/Turns/DefaultTurnRunner.cs` |
| `OperationalDiagnosticsCoordinator` | `src/SharpClaw.Code.Runtime/Diagnostics/OperationalDiagnosticsCoordinator.cs` |
| `IRuntimeCommandService` | `src/SharpClaw.Code.Runtime/Abstractions/IRuntimeCommandService.cs` |
| `SharpClawConfigService` | `src/SharpClaw.Code.Runtime/Configuration/SharpClawConfigService.cs` |
| `ShareSessionService` | `src/SharpClaw.Code.Runtime/Workflow/ShareSessionService.cs` |
| `ConversationCompactionService` | `src/SharpClaw.Code.Runtime/Workflow/ConversationCompactionService.cs` |
| `WorkspaceHttpServer` | `src/SharpClaw.Code.Runtime/Server/WorkspaceHttpServer.cs` |
