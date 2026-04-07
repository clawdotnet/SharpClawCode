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
| `session/prompt` | Runs a turn via **`IConversationRuntime`**; may stream **`session/notification`** lines with chunks before the final result. |

## Capabilities / limits

The host advertises **`loadSession: true`** and **`promptCapabilities.embeddedContext: true`**; **`image`** and **`audio`** are **`false`**.

**Intentionally unsupported** (errors or omissions vs full vendor ACP): streaming tool execution, interactive permission UI, rich media parts, MCP hot-plug, cancellation reliability guarantees, and non-core extensions. Callers should treat unknown methods as **unsupported** (JSON-RPC error).

## Session attachment

Behavior aligns with **`IWorkspaceSessionAttachmentStore`** and **`RunPromptRequest.SessionId`**: create/load sets attachment; prompts resolve cwd via params when provided.

## Implementation

See **`SharpClaw.Code.Acp`** / **`AcpStdioHost`**.
