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
- `memory: true`
- `promptCapabilities.embeddedContext: true`

`image` and `audio` remain `false`.

Notifications use `session/notification` and currently include:

- streamed assistant text chunks (`sessionUpdate = "agentMessageChunk"`)
- approval prompts (`sessionUpdate = "approvalRequest"`)

**Intentionally unsupported** (errors or omissions vs full vendor ACP): streaming tool execution details, rich media parts, MCP hot-plug, cancellation reliability guarantees, and non-core extensions. Callers should treat unknown methods as **unsupported** (JSON-RPC error).

## Session attachment

Behavior aligns with **`IWorkspaceSessionAttachmentStore`** and **`RunPromptRequest.SessionId`**: create/load sets attachment; prompts resolve cwd via params when provided.

When the client advertises approval support during `initialize`, ACP-driven prompts can participate in the same permission and approval flow as interactive CLI callers without opening a second transport.

## Implementation

See **`SharpClaw.Code.Acp`** / **`AcpStdioHost`**.
