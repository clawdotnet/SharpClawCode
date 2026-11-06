# ACP (subprocess JSON-RPC)

SharpClaw exposes a minimal **stdio JSON-RPC** host intended for IDE-style subprocess integrations (Agent Client Protocol–shaped surface).

## CLI

```bash
dotnet run --project src/SharpClaw.Code.Cli -- acp
```

Reads **one JSON-RPC request per line** from stdin; writes **one response object per line** to stdout.

## Supported methods

| Method | Notes |
|--------|--------|
| `initialize` | Returns `protocolVersion`, `agentCapabilities`, and `serverInfo`. |
| `session/new` | Creates a session; updates workspace attachment when applicable. |
| `session/load` | Loads an existing session id. |
| `session/prompt` | Runs a turn via **`IConversationRuntime`**; accepts `cwd`, `sessionId`, `prompt`, optional `model`, and optional `editorContext`. |
| `session/steer` | Queues a correction for an active editor prompt. |
| `session/cancel` | Requests cancellation of an active editor prompt. |
| `models/list` | Returns the provider catalog, including discovered local runtime profiles and models. |
| `workspace/index/refresh` | Refreshes the durable workspace knowledge index for `cwd`. |
| `workspace/search` | Executes hybrid workspace search against indexed files, symbols, and semantic chunks. |
| `memory/list` | Lists structured project/user memory entries. |
| `memory/save` | Saves a structured memory entry. |
| `memory/delete` | Deletes a structured memory entry. |
| `approval/respond` | Resolves a pending approval request emitted by the ACP host. |

## Capabilities / limits

The host advertises:

- `loadSession: true`
- `approvalRequests: true`
- `models: true`
- `workspaceSearch: true`
- `workspaceIndex: true`
- `liveTurnControl: true` when the runtime control service is available
- `memory: true`
- `promptCapabilities.embeddedContext: true`

`image` and `audio` remain `false`.

Notifications use `session/notification` and currently include:

- final assistant text (`sessionUpdate = "agentMessageChunk"`)
- approval prompts (`sessionUpdate = "approvalRequest"`)
- progress for prompts using live controls (`sessionUpdate = "turnProgress"`)

**Intentionally unsupported** (errors or omissions vs full vendor ACP): rich media parts and MCP hot-plug. Callers should treat unknown methods as **unsupported** (JSON-RPC error).

## Live editor prompts

Check `agentCapabilities.liveTurnControl` during initialization. To opt in, include `liveTurnControl: true` on `session/prompt` and use a unique JSON-RPC ID for each prompt on the connection. These SharpClaw methods use request/response JSON-RPC. Each line below is a separate request.

```json
{"jsonrpc":"2.0","id":"p1","method":"session/prompt","params":{"cwd":".","sessionId":"s1","prompt":"Inspect the parser","liveTurnControl":true}}
{"jsonrpc":"2.0","id":2,"method":"session/steer","params":{"cwd":".","sessionId":"s1","promptRequestId":"p1","text":"Preserve the public API."}}
{"jsonrpc":"2.0","id":3,"method":"session/cancel","params":{"cwd":".","sessionId":"s1","promptRequestId":"p1"}}
```

Each host instance serves one stdio connection at a time. The host matches the workspace, session, and original prompt request ID. The control result contains `accepted`. A false value means the turn has settled, the target does not match, or the steering queue rejected the message. An accepted correction enters the runtime at its next safe boundary. An accepted cancellation ends that prompt with `stopReason: "cancelled"` once execution settles.

Progress notifications identify the prompt with `params.promptRequestId` and the session with `params.sessionId`. The update has `kind` equal to `turnStarted`, `toolStarted`, `toolCompleted`, `contextCompacted`, or `steeringAccepted`. Tool updates carry the tool name, and completion reports success. Progress is best effort. The durable event log remains the source of record.

When a prompt ends before it consumes a queued correction, its response returns that text in `pendingSteering`. On failure it appears in `error.data.pendingSteering`. Clients can show those corrections for resubmission. Closing stdin cancels opted-in prompts and releases pending approval waits. Ordinary prompts continue to support batch clients that close stdin after sending their request.

## Session attachment

Behavior aligns with **`IWorkspaceSessionAttachmentStore`** and **`RunPromptRequest.SessionId`**: create/load sets attachment; prompts resolve cwd via params when provided.

When the client advertises approval support during `initialize`, ACP-driven prompts can participate in the same permission and approval flow as interactive CLI callers through the editor. When the host requires authenticated approvals, it checks the active principal and tenant before displaying the request.

## Implementation

See **`SharpClaw.Code.Acp`** / **`AcpStdioHost`**.
