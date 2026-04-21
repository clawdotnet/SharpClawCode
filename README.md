# SharpClaw Code

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![C%23 13](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![MCP](https://img.shields.io/badge/MCP-enabled-0A7EA4)](docs/mcp.md)
[![Cross-platform](https://img.shields.io/badge/platform-cross--platform-2EA44F)](docs/architecture.md)

SharpClaw Code is a C# and .NET-native coding agent runtime for teams building AI developer tools, agentic CLIs, and MCP-enabled workflows.

It combines durable sessions, permission-aware tool execution, provider abstraction, structured telemetry, and an automation-friendly command-line surface in a runtime shaped for real .NET systems: explicit, testable, and operationally legible.

The repository now ships both a terminal-first agent runtime and an embeddable host SDK through `SharpClaw.Code`, which makes it viable for standalone CLIs, local editor backends, and tenant-aware embedded services.

## What It Is

SharpClaw Code is an open-source runtime for building and operating coding-agent experiences in the .NET ecosystem.

It is designed for:

- teams that want a **C# coding agent** runtime instead of stitching together ad hoc scripts
- developers building **AI-powered CLI tools** with strong machine-readable output
- products that need **MCP integration**, plugin discovery, and permission-aware tool execution
- workflows that need **durable session state**, append-only history, and replayable runtime events
- cross-platform environments, with deliberate attention to **Windows-safe behavior**

## Why It Stands Out

- **.NET-native architecture**: built around `.NET 10`, `C# 13`, idiomatic DI, `System.Text.Json`, `System.CommandLine`, and `Spectre.Console`
- **Durable runtime model**: sessions, checkpoints, append-only event logs, export/import flows, and recovery-aware orchestration
- **Safety by default**: permission modes, approval gates, workspace-boundary enforcement, and mediated tool execution
- **Extensible surface**: providers, MCP servers, plugins, skills, ACP hosting, and runtime commands integrate through explicit seams
- **Automation-ready interface**: stable JSON output, operational commands, and a clean CLI-first workflow that works for both humans and scripts

## Quick Start

### Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: [GitHub CLI](https://cli.github.com/) for release and repo automation

### Build and test

```bash
git clone https://github.com/clawdotnet/SharpClawCode.git
cd SharpClawCode
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

### Run the CLI

```bash
dotnet run --project src/SharpClaw.Code.Cli -- [arguments]
```

## Common CLI Flows

```bash
# Start the interactive REPL
dotnet run --project src/SharpClaw.Code.Cli
dotnet run --project src/SharpClaw.Code.Cli -- repl

# Run a one-shot prompt
dotnet run --project src/SharpClaw.Code.Cli -- prompt "Summarize this workspace"
dotnet run --project src/SharpClaw.Code.Cli -- --auto-approve shell --auto-approve-budget 2 prompt "Check git status and summarize"

# Inspect runtime health and status
dotnet run --project src/SharpClaw.Code.Cli -- doctor
dotnet run --project src/SharpClaw.Code.Cli -- status

# Refresh and query the workspace knowledge index
dotnet run --project src/SharpClaw.Code.Cli -- index refresh
dotnet run --project src/SharpClaw.Code.Cli -- index query WidgetService

# Save and inspect durable memory
dotnet run --project src/SharpClaw.Code.Cli -- memory save --scope project "Keep prompts concise"
dotnet run --project src/SharpClaw.Code.Cli -- memory list --scope project

# Inspect metering summaries and details
dotnet run --project src/SharpClaw.Code.Cli -- usage summary
dotnet run --project src/SharpClaw.Code.Cli -- usage detail --limit 25

# Manage packaged tool bundles
dotnet run --project src/SharpClaw.Code.Cli -- tool-packages list

# Emit machine-readable output
dotnet run --project src/SharpClaw.Code.Cli -- --output-format json doctor
```

Built-in REPL slash commands include `/help`, `/status`, `/doctor`, `/session`, `/commands`, `/mode`, `/editor`, `/export`, `/undo`, `/redo`, and `/version`. Use `/help` to see the active command set, including discovered workspace custom commands.

Parity-oriented commands now include:

- `index` / `/index`
- `memory` / `/memory`
- `models` / `/models`
- `usage` / `/usage`
- `cost` / `/cost`
- `stats` / `/stats`
- `connect` / `/connect`
- `hooks` / `/hooks`
- `skills` / `/skills`
- `agents` / `/agents`
- `todo` / `/todo`
- `share` / `/share`
- `unshare` / `/unshare`
- `compact` / `/compact`
- `serve` / `/serve`
- `worktree` / `/worktree`
- `/sessions` as a friendlier alias over `/session list`

Primary workflow modes:

- `build`: normal coding-agent execution
- `plan`: structured deep planning that blocks mutating tools and syncs planning-owned session todos
- `spec`: generates Kiro-style spec artifacts under `docs/superpowers/specs/<date>-<slug>/`

## Core Capabilities

| Capability | Why it matters |
|---|---|
| Durable sessions | Persist conversation state, turn history, checkpoints, and recovery metadata for longer-running agent work |
| Permission-aware tools | Route file, shell, and plugin-backed actions through explicit policy and approval decisions |
| Provider abstraction | Run against Anthropic and OpenAI-compatible backends through a typed runtime surface |
| Local runtime catalog | Surface Ollama, llama.cpp, and other OpenAI-compatible profiles with health, model discovery, and embedding defaults |
| MCP support | Register, supervise, and integrate MCP servers with explicit lifecycle state |
| Plugins and skills | Extend the runtime with trusted plugin manifests and discoverable workspace skills |
| Workspace knowledge | Build a durable local index for lexical, symbol, and semantic workspace search |
| Cross-session memory | Persist project and user memory so later sessions can recall repo-specific guidance and user preferences |
| Structured telemetry | Emit runtime events and usage signals that support diagnostics, replay, and automation |
| Enterprise host controls | Add tenant-aware storage, authenticated approvals, admin APIs, and usage metering for embedded deployments |
| JSON-friendly CLI | Use the same runtime through human-readable terminal flows or machine-readable command output |
| Spec workflow mode | Turn prompts into structured requirements, technical design, and task documents for feature proposals |
| Embedded SDK + server | Host the runtime via `SharpClaw.Code` or expose prompt, session, admin, and SSE endpoints for editor or automation clients |
| Config + agent catalog | Layer user/workspace JSONC config with typed agent defaults, tool allowlists, and runtime hooks |
| Session sharing | Create self-hosted share links and durable sanitized share snapshots under `.sharpclaw/` |
| Diagnostics context | Surface configured diagnostics sources into prompt context, status, and machine-readable output |

## Good Fit For

- building a **C# AI coding assistant**
- running a **local or hosted coding-agent CLI**
- creating a **.NET MCP client/runtime**
- adding **session persistence and auditability** to agent workflows
- building on **Microsoft Agent Framework** without pushing your application core into framework-specific code

## Solution Layout

| Area | Project(s) |
|---|---|
| Embeddable SDK | `SharpClaw.Code` |
| CLI and command handlers | `SharpClaw.Code.Cli`, `SharpClaw.Code.Commands` |
| Core contracts | `SharpClaw.Code.Protocol` |
| Runtime orchestration | `SharpClaw.Code.Runtime` |
| Agents | `SharpClaw.Code.Agents` |
| Tools and permissions | `SharpClaw.Code.Tools`, `SharpClaw.Code.Permissions` |
| ACP host | `SharpClaw.Code.Acp` |
| Providers, MCP, plugins | `SharpClaw.Code.Providers`, `SharpClaw.Code.Mcp`, `SharpClaw.Code.Plugins` |
| Sessions, telemetry, infrastructure | `SharpClaw.Code.Sessions`, `SharpClaw.Code.Telemetry`, `SharpClaw.Code.Infrastructure` |
| Memory, git, web, skills | `SharpClaw.Code.Memory`, `SharpClaw.Code.Git`, `SharpClaw.Code.Web`, `SharpClaw.Code.Skills` |

For dependency boundaries and project responsibilities, see [docs/architecture.md](docs/architecture.md) and [AGENTS.md](AGENTS.md).

## Example Hosts

The solution includes embeddable host samples under `examples/`:

- `MinimalConsoleAgent` for direct SDK prompt execution
- `WebApiAgent` for an HTTP-hosted runtime surface
- `WorkerServiceHost` for lifecycle-managed background hosting
- `McpToolAgent` for MCP-aware host composition

## Testing

```bash
# Run all tests
dotnet test SharpClawCode.sln

# Build example hosts as part of local validation
dotnet build examples/WebApiAgent/WebApiAgent.csproj
dotnet build examples/MinimalConsoleAgent/MinimalConsoleAgent.csproj
dotnet build examples/WorkerServiceHost/WorkerServiceHost.csproj
dotnet build examples/McpToolAgent/McpToolAgent.csproj

# Run a single test by name
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~YourTestName"

# Run parity harness scenarios only
dotnet test SharpClawCode.sln --filter "FullyQualifiedName~ParityScenarioTests"
```

| Project | Purpose |
|---|---|
| `SharpClaw.Code.UnitTests` | Fast unit tests covering tools, permissions, sessions, providers, state machine, telemetry, and serialization |
| `SharpClaw.Code.IntegrationTests` | Runtime and provider flows with full DI composition |
| `SharpClaw.Code.ParityHarness` | End-to-end scenarios using a deterministic mock LLM provider |
| `SharpClaw.Code.MockProvider` | `DeterministicMockModelProvider` with named scenarios for reproducible testing |
| `SharpClaw.Code.Mcp.FixtureServer` | MCP fixture server for integration testing |

## Global CLI Options

| Option | Description |
|---|---|
| `--cwd <path>` | Working directory; defaults to the current directory |
| `--model <id>` | Model id or alias; `provider/model` forms are supported where configured |
| `--permission-mode <mode>` | `readOnly`, `workspaceWrite`, or `dangerFullAccess`; see [docs/permissions.md](docs/permissions.md) |
| `--auto-approve <scopes>` | Auto-approve specific elevated scopes such as `shell`, `network`, or `promptRead` |
| `--auto-approve-budget <n>` | Cap how many elevated operations may be auto-approved in the session |
| `--output-format text\|json` | Human-readable or structured output |
| `--primary-mode <mode>` | Workflow bias for prompts: `build`, `plan`, or `spec` |
| `--session <id>` | Reuse a specific SharpClaw session id for prompt execution |
| `--agent <id>` | Select the active agent for prompt execution |
| `--host-id <id>` | Stable embedded-host identifier for metering, admin, and event envelopes |
| `--tenant-id <id>` | Tenant identifier used by host-aware storage, approvals, and metering |
| `--storage-root <path>` | External root for host-managed durable runtime state |
| `--session-store fileSystem\|sqlite` | Select the embedded session/event storage backend |

Subcommands include `prompt`, `repl`, `doctor`, `status`, `session`, `index`, `memory`, `models`, `usage`, `cost`, `stats`, `connect`, `hooks`, `skills`, `agents`, `todo`, `share`, `unshare`, `compact`, `serve`, `commands`, `worktree`, `mcp`, `plugins`, `tool-packages`, `acp`, `bridge`, and `version`.

## Documentation Map

| Doc | Topic |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Solution layout, dependencies, and execution flows |
| [docs/runtime.md](docs/runtime.md) | Turns, `ConversationRuntime`, and runtime coordinators |
| [docs/sessions.md](docs/sessions.md) | Session snapshots and append-only event logs |
| [docs/providers.md](docs/providers.md) | `IModelProvider`, configuration, and provider integration |
| [docs/tools.md](docs/tools.md) | Tool registry, executor behavior, and built-ins |
| [docs/permissions.md](docs/permissions.md) | Permission modes, policies, and approvals |
| [docs/agents.md](docs/agents.md) | Agent Framework integration |
| [docs/mcp.md](docs/mcp.md) | MCP registration, lifecycle, and orchestration |
| [docs/acp.md](docs/acp.md) | ACP stdio host and protocol notes |
| [docs/plugins.md](docs/plugins.md) | Plugin discovery, trust, and CLI flows |
| [docs/testing.md](docs/testing.md) | Unit, integration, and parity-harness coverage |
| [ARCHITECTURE-NOTES.md](ARCHITECTURE-NOTES.md) | Architectural follow-ups and cleanup ideas |

## Configuration

SharpClaw Code uses both the standard .NET configuration stack (`appsettings.json`, environment variables, CLI args) and layered SharpClaw JSONC config files:

- user config: `~/.config/sharpclaw/config.jsonc` on Unix-like systems
- Windows user config: `%AppData%\\SharpClaw\\config.jsonc`
- workspace config: `<workspace>/sharpclaw.jsonc`

Precedence is:

`CLI args > workspace sharpclaw.jsonc > user config.jsonc > appsettings/environment defaults`

Key runtime configuration sections:

| Section | Purpose |
|---|---|
| `SharpClaw:Providers:Catalog` | Default provider, model aliases |
| `SharpClaw:Providers:Anthropic` | Anthropic API key, base URL, default model |
| `SharpClaw:Providers:OpenAiCompatible` | OpenAI-compatible base settings plus local runtime profiles, auth mode, and default embedding model |
| `SharpClaw:Web` | Web search provider name, endpoint template, user agent |
| `SharpClaw:Telemetry` | Runtime event ring buffer capacity plus webhook event export behavior |

Key `sharpclaw.jsonc` capabilities:

- `shareMode`: `manual`, `auto`, or `disabled`
- `server`: host, port, and optional public base URL for share links
- `defaultAgentId`: default prompt agent
- `agents`: typed agent catalog entries with model defaults, tool allowlists, and instruction appendices
- `lspServers`: configured diagnostics sources
- `hooks`: lifecycle hooks for turn/tool/share/server events
- `connectLinks`: browser entry points for provider or external auth flows

All options are validated at startup via `IValidateOptions` implementations.

## Current Scope

- The shared tooling layer is permission-aware across the runtime.
- The current runtime includes multi-turn provider-backed tool execution, session-backed prompt replay, and durable conversation history.
- Agent-driven tool calls flow through the same approval and allowlist enforcement path used by direct tool execution, including caller-aware interactive approval behavior.
- Workspace indexing, symbol search, and durable memory are available through both CLI commands and built-in tools.
- ACP now carries editor context, approval round-trips, model catalog queries, workspace search/index actions, and memory actions, which is enough for a real VS Code client over a single transport.
- OpenAI-compatible local runtime profiles can surface Ollama, llama.cpp, and similar endpoints with profile-aware auth and model discovery.
- Embedded hosts can opt into trusted-header or OIDC-backed approval identity, tenant-aware usage metering, webhook/SSE event streaming, and admin APIs for provider catalog, index status, search, memory inspection, and tool package management.
- The CLI mirrors those enterprise surfaces with `usage summary`, `usage detail`, and `tool-packages` commands while preserving the existing workspace-local `usage`, `cost`, and `stats` flows.
- Operational commands support stable JSON output via `--output-format json`, which makes them suitable for scripts, editors, and automation.
- The embedded server exposes local JSON and SSE endpoints for prompts, sessions, admin control, metering, and event streaming.

## Contributing

Use `.NET 10` and `C# 13`. Follow [AGENTS.md](AGENTS.md) for project boundaries, serialization conventions, logging expectations, and cross-platform rules.

Before opening a PR:

```bash
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
dotnet build examples/WebApiAgent/WebApiAgent.csproj
dotnet build examples/MinimalConsoleAgent/MinimalConsoleAgent.csproj
dotnet build examples/WorkerServiceHost/WorkerServiceHost.csproj
dotnet build examples/McpToolAgent/McpToolAgent.csproj
```

## License

This project is licensed under the [MIT License](LICENSE).

Repository: [github.com/clawdotnet/SharpClawCode](https://github.com/clawdotnet/SharpClawCode)
