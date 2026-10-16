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

Listeners are restricted to loopback and configured explicitly, independently of workspace `appsettings.json` and Kestrel environment settings. Non-loopback plaintext binds are rejected, including with the reserved `--allow-remote` option. For remote access, run a TLS reverse proxy on the same machine and forward requests to the loopback listener. Set `--allowed-host mcp.example.com` (repeatable; embedded hosts use `AllowedHosts`) and configure the proxy to preserve that HTTPS Host authority. A nondefault HTTPS port can be included, such as `mcp.example.com:8443`. Wildcards, paths, and arbitrary forwarded headers are rejected. Allowed proxy hosts require a token even in read-only mode. Origin validation requires the matching HTTPS origin for a proxy hostname; default ports are normalized. The token therefore crosses the remote network only inside the proxy's TLS connection.

The framework-dependent CLI tool and aggregate `SharpClaw.Code` package require the .NET 10 ASP.NET Core shared runtime because they include the HTTP MCP host. Installing the .NET 10 SDK supplies it. Self-contained native archives include this runtime.
