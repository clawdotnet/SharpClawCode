# SharpClaw Code

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![C%23 13](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![MCP](https://img.shields.io/badge/MCP-enabled-0A7EA4)](docs/mcp.md)
[![Cross-platform](https://img.shields.io/badge/platform-cross--platform-2EA44F)](docs/architecture.md)

**OpenCode meets Claude Code in C#**.

SharpClaw Code is a .NET-native coding agent runtime for teams who want modern agent ergonomics, explicit orchestration, and production-grade architecture in the C# ecosystem.

It is designed for cross-platform use, strong Windows compatibility, permission-aware tooling infrastructure, durable sessions, MCP and plugin extensibility, and machine-readable CLI workflows that scale beyond a toy prototype.

## Why SharpClaw Code

- **C# and .NET first**: built around `.NET 10`, `C# 13`, idiomatic dependency injection, and `System.Text.Json`.
- **Agent-runtime ready**: durable sessions, append-only history, runtime lifecycle control, and structured telemetry.
- **Tooling with guardrails**: explicit permission modes, mediated tool execution paths, and clear operational boundaries.
- **Extensible by design**: model providers, MCP servers, plugins, and skills can plug into the same runtime surface.
- **Built for real workflows**: REPL, one-shot prompts, diagnostics, status inspection, and automation-friendly CLI behavior.

## Requirements

- [.NET SDK **10**](https://dotnet.microsoft.com/download/dotnet/10.0)
- `C# 13`
- Optional: [GitHub CLI](https://cli.github.com/) (`gh`) for releases and repository automation

## Quick Start

```bash
git clone https://github.com/clawdotnet/SharpClawCode.git
cd SharpClawCode
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

Run the CLI from the repository root:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- [arguments]
```

## CLI Examples

```bash
# Interactive REPL (default when no subcommand)
dotnet run --project src/SharpClaw.Code.Cli
dotnet run --project src/SharpClaw.Code.Cli -- repl

# One-shot prompt
dotnet run --project src/SharpClaw.Code.Cli -- prompt "Summarize this workspace"

# Operational diagnostics
dotnet run --project src/SharpClaw.Code.Cli -- doctor
dotnet run --project src/SharpClaw.Code.Cli -- status

# Machine-readable output
dotnet run --project src/SharpClaw.Code.Cli -- --output-format json doctor
```

Built-in slash commands in the REPL include `/help`, `/status`, `/doctor`, `/session`, `/commands`, `/mode`, `/editor`, `/export`, `/undo`, `/redo`, and `/version`. Use `/help` to see the current built-in list plus any workspace-defined custom commands, and `/exit` to leave interactive mode.

## What You Get

| Capability | What it means |
|------------|----------------|
| Durable sessions | Persist conversation state, turn history, and recovery data for longer-running agent work |
| Permission-aware tools | Built-in and plugin-backed tools run through explicit permission policies instead of hidden side effects |
| Provider abstraction | Work with Anthropic and OpenAI-compatible backends through a typed runtime surface |
| MCP and plugins | Extend the runtime with MCP servers, plugin discovery, and lifecycle-aware integrations |
| Structured output | Support human-readable CLI flows and JSON-friendly automation from the same commands |

## Solution Layout

| Area | Project(s) |
|------|------------|
| CLI | `SharpClaw.Code.Cli`, `SharpClaw.Code.Commands` |
| Core contracts | `SharpClaw.Code.Protocol` |
| Runtime and orchestration | `SharpClaw.Code.Runtime` |
| Agents | `SharpClaw.Code.Agents` |
| Tools and permissions | `SharpClaw.Code.Tools`, `SharpClaw.Code.Permissions` |
| ACP host | `SharpClaw.Code.Acp` |
| Providers, MCP, and plugins | `SharpClaw.Code.Providers`, `SharpClaw.Code.Mcp`, `SharpClaw.Code.Plugins` |
| Memory, git, web, and skills | `SharpClaw.Code.Memory`, `SharpClaw.Code.Git`, `SharpClaw.Code.Web`, `SharpClaw.Code.Skills` |
| Sessions, telemetry, and infrastructure | `SharpClaw.Code.Sessions`, `SharpClaw.Code.Telemetry`, `SharpClaw.Code.Infrastructure` |

For dependency boundaries and project responsibilities, see [docs/architecture.md](docs/architecture.md) and [AGENTS.md](AGENTS.md).

## Global CLI Options

| Option | Description |
|--------|-------------|
| `--cwd <path>` | Working directory; defaults to the current directory |
| `--model <id>` | Model id or alias; `provider/model` forms are supported where configured |
| `--permission-mode <mode>` | `readOnly`, `workspaceWrite`, or `dangerFullAccess`; see [docs/permissions.md](docs/permissions.md) |
| `--output-format text\|json` | Human-readable or structured output |
| `--primary-mode <mode>` | Workflow bias for prompts: `build` or `plan` |
| `--session <id>` | Reuse a specific SharpClaw session id for prompt execution |

Subcommands include `prompt`, `repl`, `doctor`, `status`, `session`, `commands`, `mcp`, `plugins`, `acp`, `bridge`, and `version`. Workspace custom commands can also appear as top-level commands when discovered.

Supported `--output-format` values today are `text` and `json`. Unknown global option values currently fall back to defaults, so scripts should pass exact values.

## Documentation

| Doc | Topic |
|-----|-------|
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
| [docs/migrations-deferred.md](docs/migrations-deferred.md) | Deferred migrations and follow-up work |
| [ARCHITECTURE-NOTES.md](ARCHITECTURE-NOTES.md) | Architectural follow-ups and future cleanup ideas |

## Current Scope Notes

- The shared tooling layer is permission-aware and available across the runtime, but the current Microsoft Agent Framework bridge still focuses on provider-backed runs rather than a full tool-calling loop inside the chat framework path.
- Operational commands support stable JSON output envelopes via `--output-format json`, making them suitable for scripting and automation.

## Contributing

Use **.NET 10** and **C# 13**. Follow [AGENTS.md](AGENTS.md) for project boundaries, JSON conventions (`System.Text.Json` only), logging, and cross-platform behavior. Before opening a PR, make sure the solution builds and the relevant tests pass:

```bash
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

Repository: [github.com/clawdotnet/SharpClawCode](https://github.com/clawdotnet/SharpClawCode)
