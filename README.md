# SharpClaw Code

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![C%23 13](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![MCP](https://img.shields.io/badge/MCP-enabled-0A7EA4)](docs/mcp.md)
[![Cross-platform](https://img.shields.io/badge/platform-cross--platform-2EA44F)](docs/architecture.md)

SharpClaw Code is a C# and .NET-native coding agent runtime for teams building AI developer tools, agentic CLIs, and MCP-enabled workflows.

It combines durable sessions, permission-aware tool execution, provider abstraction, structured telemetry, and an automation-friendly command-line surface in a codebase designed for production-quality .NET systems, not toy demos.

## What SharpClaw Code Is

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
- **Good fit for automation**: JSON-friendly command output, stable operational commands, and a clean CLI-first workflow

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

# Inspect runtime health and status
dotnet run --project src/SharpClaw.Code.Cli -- doctor
dotnet run --project src/SharpClaw.Code.Cli -- status

# Emit machine-readable output
dotnet run --project src/SharpClaw.Code.Cli -- --output-format json doctor
```

Built-in REPL slash commands include `/help`, `/status`, `/doctor`, `/session`, `/commands`, `/mode`, `/editor`, `/export`, `/undo`, `/redo`, and `/version`. Use `/help` to see the active command set, including discovered workspace custom commands.

Primary workflow modes:

- `build`: normal coding-agent execution
- `plan`: analysis-first mode that blocks mutating tools
- `spec`: generates Kiro-style spec artifacts under `docs/superpowers/specs/<date>-<slug>/`

## Core Capabilities

| Capability | Why it matters |
|---|---|
| Durable sessions | Persist conversation state, turn history, checkpoints, and recovery metadata for longer-running agent work |
| Permission-aware tools | Route file, shell, and plugin-backed actions through explicit policy and approval decisions |
| Provider abstraction | Run against Anthropic and OpenAI-compatible backends through a typed runtime surface |
| MCP support | Register, supervise, and integrate MCP servers with explicit lifecycle state |
| Plugins and skills | Extend the runtime with trusted plugin manifests and discoverable workspace skills |
| Structured telemetry | Emit runtime events and usage signals that support diagnostics, replay, and automation |
| JSON-friendly CLI | Use the same runtime through human-readable terminal flows or machine-readable command output |
| Spec workflow mode | Turn prompts into structured requirements, technical design, and task documents for feature proposals |

## Good Fit For

- building a **C# AI coding assistant**
- running a **local or hosted coding-agent CLI**
- creating a **.NET MCP client/runtime**
- adding **session persistence and auditability** to agent workflows
- experimenting with **Agent Framework-backed orchestration** without pushing core runtime logic into the framework layer

## Solution Layout

| Area | Project(s) |
|---|---|
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

## Global CLI Options

| Option | Description |
|---|---|
| `--cwd <path>` | Working directory; defaults to the current directory |
| `--model <id>` | Model id or alias; `provider/model` forms are supported where configured |
| `--permission-mode <mode>` | `readOnly`, `workspaceWrite`, or `dangerFullAccess`; see [docs/permissions.md](docs/permissions.md) |
| `--output-format text\|json` | Human-readable or structured output |
| `--primary-mode <mode>` | Workflow bias for prompts: `build`, `plan`, or `spec` |
| `--session <id>` | Reuse a specific SharpClaw session id for prompt execution |

Subcommands include `prompt`, `repl`, `doctor`, `status`, `session`, `commands`, `mcp`, `plugins`, `acp`, `bridge`, and `version`.

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

## Current Scope

- The shared tooling layer is permission-aware across the runtime.
- The current Agent Framework bridge is focused on provider-backed runs rather than a full tool-calling loop inside the framework path.
- Operational commands support stable JSON output via `--output-format json`, which makes them useful in scripts and automation.

## Contributing

Use `.NET 10` and `C# 13`. Follow [AGENTS.md](AGENTS.md) for project boundaries, serialization conventions, logging expectations, and cross-platform rules.

Before opening a PR:

```bash
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

Repository: [github.com/clawdotnet/SharpClawCode](https://github.com/clawdotnet/SharpClawCode)
