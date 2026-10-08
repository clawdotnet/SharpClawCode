# Inbound MCP server

`SharpClaw.Code.Mcp.Server` exposes the existing tool executor through the official MCP SDK. Register `AddSharpClawRuntime` and `AddSharpClawMcpServer` for an embedded host, then run `ISharpClawMcpServer.RunAsync` with a fixed workspace and explicit options. The server does not start the agent runtime, scheduler, outbound MCP clients, or provider authentication.

```shell
sharpclaw mcp serve --cwd ./workspace
```

Stdio is the default. Stdout contains only MCP messages. The server defaults to `readOnly`, even though ordinary CLI commands default to `workspaceWrite`. It advertises `workspace_search`, `symbol_search`, `dotnet_solution_inspect`, `csharp_symbol_resolve`, `csharp_find_references`, `csharp_type_hierarchy`, `csharp_diagnostics`, and `verify_workspace`.

Read-only queries can use an approved semantic cache, but a cold MSBuild load needs execution permission because project targets can run code. Verification is also an execution operation and is denied by the default mode. Permission denials return MCP tool errors and retain structured SharpClaw reports. Hosting never opens interactive approval prompts.

Explicitly permit the required scopes to evaluate and verify a trusted workspace:

```shell
sharpclaw mcp serve --cwd ./workspace --permission-mode workspaceWrite --auto-approve shell
```

Rename requires both `--allow-mutations` and a permission mode that permits writes; approval-sensitive writes additionally need a host-approved file scope. Hidden tools remain unavailable when called by name. Tool arguments cannot change the workspace, host identity, or permissions.

```shell
sharpclaw mcp serve --cwd ./workspace --permission-mode workspaceWrite --auto-approve shell,file --allow-mutations
```

Elevated clients get independent durable SharpClaw sessions. Rename records checkpoint-linked mutation sets, including residual changes after failure or cancellation, so the normal session undo/redo surface can recover them. Client calls are serialized within their session, and semantic writes and verification also acquire workspace locks. HTTP and stdio share this bridge. Existing outbound MCP lifecycle and trust rules retain their behavior.

## Streamable HTTP

```shell
sharpclaw mcp serve --cwd ./workspace --transport http --port 7346
```

Connect to `http://127.0.0.1:7346/mcp`. HTTP validates the Host header and any Origin header before the MCP endpoint. It uses stateful SDK sessions, with five-minute idle expiry and at most 64 idle sessions. Cancellation and shutdown dispose the transport. Each elevated client receives its own SharpClaw session and approval accounting.

Set `SHARPCLAW_MCP_TOKEN` to a secret containing at least 16 non-whitespace characters before enabling elevated HTTP permissions. Clients must send `Authorization: Bearer <token>` on all requests. Embedded hosts can supply `BearerToken` directly. Tokens are not accepted as CLI arguments. A configured token is enforced even in read-only mode.

Remote binding requires an explicit IP, `--allow-remote`, and authentication. Wildcard hosts are rejected. Deploy remote access behind a TLS endpoint. This host-selected token uses existing tenant/storage context and scoped approvals; it does not introduce a separate identity system.
