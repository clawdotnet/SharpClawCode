# MCP

**Project:** `SharpClaw.Code.Mcp`  
**Registration:** `McpServiceCollectionExtensions.AddSharpClawMcp` (pulled in by **`AddSharpClawRuntime`**)

## Official SDK

MCP sessions use the **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)** NuGet package (official MCP C# SDK, version pinned in **`Directory.Packages.props`**). SharpClaw keeps **registry, CLI, Protocol DTOs, and diagnostics** as first-party code; **`SdkMcpProcessSupervisor`** drives:

- **stdio** — **`StdioClientTransport`** + **`McpClient`** (subprocess; `--command` is the executable, `--arg` for arguments).
- **http** / **https** — **`HttpClientTransport`** with **`HttpTransportMode.AutoDetect`** (streamable HTTP with SSE fallback); **`--command`** must be an absolute URL (for example `https://host/mcp`).
- **streamable-http** — **`HttpTransportMode.StreamableHttp`** explicitly.
- **sse** — **`HttpTransportMode.Sse`** for legacy SSE endpoints.

Initialize, session lifetime, and capability discovery use **`ListTools`** / **`ListPrompts`** / **`ListResources`** counts.

Active sessions are stopped by disposing the SDK client via an opaque **`SessionHandle`** on **`McpServerStatus`**. The official stdio client does not expose the child process **Pid** on its public surface; use **`SessionHandle`** for **`mcp stop`**. HTTP transports have no OS process **Pid**.

## Workspace storage

**`FileBackedMcpRegistry`** persists definitions and status under:

`{workspace}/.sharpclaw/mcp/servers.json`

(JSON with **`McpServerDefinition`** map + **`McpServerStatus`** map — Web defaults; indented.)

**`RegisteredMcpServer`** (Protocol **+** status) is returned from **`IMcpRegistry`** register/list/get operations.

## Core abstractions

| Interface | Role |
|-----------|------|
| **`IMcpRegistry`** | Register/list/get servers, **`UpdateStatusAsync`** |
| **`IMcpServerHost`** | **`StartAsync`**, **`StopAsync`**, **`RestartAsync`**, **`GetStatusAsync`** |
| **`IMcpDoctorService`** | Workspace MCP diagnostics → **`CommandResult`** |
| **`IMcpProcessSupervisor`** | SDK-backed sessions (`SdkMcpProcessSupervisor`: stdio + HTTP/SSE) |

Lifecycle state is tracked in **`McpServerStatus`** (Protocol **`McpLifecycleState`**, **`McpFailureKind`**, **`SessionHandle`**, tool/prompt/resource counts, etc.).

## CLI

Subcommands of **`mcp`** (see **`McpCommandHandler`**):

- **`list`** / **`status`** — delegate to **`IMcpDoctorService.GetStatusAsync`**
- **`register`** — `--id`, `--name`, `--command` (executable or absolute MCP URL), optional `--transport` (`stdio`, `http`, `https`, `streamable-http`, `sse`; default `stdio`), repeatable `--arg`, `--enabled`
- **`start`**, **`stop`**, **`restart`** — `--id`; **`IMcpServerHost`**
- **`doctor`** — **`IMcpDoctorService.RunDoctorAsync`**

Global CLI options apply (`--cwd`, `--output-format`, …).

JSON **`DataJson`** for register/start/stop/restart uses **`ProtocolJsonContext`** where types are **`RegisteredMcpServer`** / **`McpServerStatus`**. MCP doctor/status payloads serialize **`failureKind`** with the protocol enum names (`none`, `startup`, `handshake`, `capabilities`, `runtime`).

**`McpDoctorService`** still builds some diagnostic payloads with anonymous objects (see **`ARCHITECTURE-NOTES.md`**).

## Diagnostics

Runtime **doctor** includes **`McpRegistryHealthCheck`** (registry + optional host).
