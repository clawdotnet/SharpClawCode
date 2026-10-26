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

## Runnable Parallel Search example

`examples/McpToolAgent` includes an opt-in command for searching and fetching public
web content with [Parallel Search MCP](https://docs.parallel.ai/integrations/mcp/search-mcp).
It connects to `https://search.parallel.ai/mcp` using the official SDK's
Streamable HTTP transport. The anonymous endpoint needs no Parallel API key and
is free for exploration and light use, subject to rate limits.

From the repository root, with the .NET 10 SDK installed:

```shell
dotnet restore examples/McpToolAgent/McpToolAgent.csproj
dotnet build examples/McpToolAgent/McpToolAgent.csproj --configuration Release
dotnet run --project examples/McpToolAgent --configuration Release --no-build -- --parallel-search search "C# CancellationToken documentation"
dotnet run --project examples/McpToolAgent --configuration Release --no-build -- --parallel-search fetch https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
```

Each command discovers the remote tool schema, registers it as
`parallel_web_search` or `parallel_web_fetch` in a SharpClaw `ToolRegistry`, and
dispatches it through `ToolExecutor` with a read-only permission context and an
explicit tool allowlist. Search prints source URLs and excerpts; fetch prints
extracted page content. Remote tool errors produce a failing exit code.
The example identifies its requests with the User-Agent
`SharpClaw.Code-McpToolAgent/0.1.0`, passes cancellation through discovery and
execution, and bounds each command to 60 seconds. Ctrl+C cancels the request.

These commands execute tools directly, without a model or an agent reasoning
loop. They do not load the sample's model-provider configuration or saved
credentials. Running `McpToolAgent` without `--parallel-search` still runs the
original provider-backed echo example. The built-in `web_search` and `web_fetch`
tools and workspace MCP registrations are unaffected.

## Runnable deed.page publishing example

`examples/McpToolAgent` also includes an opt-in command that publishes a local
folder as a static website through [deed.page](https://deed.page)'s keyless MCP
server at `https://deed.page/mcp` ([docs](https://deed.page/docs)). No account or
API key is needed. Unlike the search example, this tool writes to the network:
**the published site is public**, so anyone with its URL can view it.

From the repository root, with the .NET 10 SDK installed:

```shell
dotnet build examples/McpToolAgent/McpToolAgent.csproj --configuration Release
dotnet run --project examples/McpToolAgent --configuration Release --no-build -- --deed-page ./site 3600
```

The folder must contain an `index.html`. Hidden files and folders such as `.git`
or `.env` are skipped, UTF-8 text is sent as text and other files as base64, and
folders over 10 MB are rejected before any request is made. The optional second
argument is an expiry in seconds; omit it for a permanent site.

The command discovers the remote `publish_site` schema and registers it as
`deed_page_publish_site` with the `NetworkAccess` approval scope. It runs in
`WorkspaceWrite` mode with an explicit allowlist, so the console approval prompt
must be answered `y` before anything is uploaded; any other answer exits with a
failing code and no publish request. `ReadOnly` mode blocks the tool. The tool
result includes the site URL and a one-time management token; keep the token if
you want to update or delete the site later (`delete_site` on the same server).
