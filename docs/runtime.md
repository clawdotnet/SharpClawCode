# Runtime

The **runtime** layer is centered on **`SharpClaw.Code.Runtime`** and especially **`ConversationRuntime`**, which implements:

- Session surface on **`IConversationRuntime`** — create/get session, **`RunPromptAsync`**
- **`IRuntimeCommandService`** — **`ExecutePromptAsync`**, **`GetStatusAsync`**, **`RunDoctorAsync`**, **`InspectSessionAsync`**

Registration: `RuntimeServiceCollectionExtensions.AddSharpClawRuntime`.

## Turn execution

**`DefaultTurnRunner`** is the **`ITurnRunner`** implementation used for prompt turns. It:

1. Calls **`IPromptContextAssembler.AssembleAsync`** to build **`PromptContext`** (prompt text, metadata such as resolved **`model`**).
2. Maps **`RunPromptRequest`** + session into **`AgentRunContext`** (session/turn ids, working directory, permission mode, output format, **`IToolExecutor`**, metadata).
3. Invokes **`PrimaryCodingAgent.RunAsync`**.

The agent stack is described in [agents.md](agents.md).

## Lifecycle and state

- **`IRuntimeStateMachine`** (`DefaultRuntimeStateMachine`) transitions **`ConversationSession.State`**.
- Failures (including **`ProviderExecutionException`** and cancellation paths) update session state and append **`SessionStateChangedEvent`** (and related events) when **`AppendEventAsync`** runs with persistence enabled.

## Context assembly

**`PromptContextAssembler`** pulls workspace/session-aware data (skills registry, memory hooks, git context as wired today) into the prompt path before the agent runs.

## Operational diagnostics

**`OperationalDiagnosticsCoordinator`** runs injectable **`IOperationalCheck`** implementations:

- Workspace, configuration, session store, shell, git, provider auth, MCP registry/host, plugin registry.

Used by **`GetStatusAsync`**, **`RunDoctorAsync`**, and **`InspectSessionAsync`** to build **Protocol** reports (`DoctorReport`, `RuntimeStatusReport`, `SessionInspectionReport`).

## Hosted service

**`RuntimeCoordinatorHostedServiceAdapter`** is registered as **`IHostedService`** and currently logs start/stop only (placeholder for future lifecycle coordination).

## Key types (file locations)

| Type | Project / path |
|------|----------------|
| `ConversationRuntime` | `src/SharpClaw.Code.Runtime/Orchestration/ConversationRuntime.cs` |
| `DefaultTurnRunner` | `src/SharpClaw.Code.Runtime/Turns/DefaultTurnRunner.cs` |
| `OperationalDiagnosticsCoordinator` | `src/SharpClaw.Code.Runtime/Diagnostics/OperationalDiagnosticsCoordinator.cs` |
| `IRuntimeCommandService` | `src/SharpClaw.Code.Runtime/Abstractions/IRuntimeCommandService.cs` |
