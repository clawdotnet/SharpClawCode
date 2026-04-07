# SharpClaw Code

A **C#-native coding-agent harness** inspired by the Rust [`claw-code`](https://github.com/claw-lang/claw-code) surface: durable sessions, tool execution with permissions, providers (Anthropic and OpenAI-compatible), MCP, plugins, and a cross-platform CLI.

Maintained by [**clawdotnet**](https://github.com/clawdotnet) on GitHub.

## Requirements

- [.NET SDK **10**](https://dotnet.microsoft.com/download/dotnet/10.0) (C# 13)
- Optional: [GitHub CLI](https://cli.github.com/) (`gh`) for releases and automation

## Quick start

```bash
git clone https://github.com/clawdotnet/SharpClawCode.git
cd SharpClawCode
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

Run the CLI from the repo root:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- [arguments]
```

### Examples

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

Slash commands in the REPL include `/help`, `/exit`, `/status`, `/doctor`, `/session`, `/version`.

## Solution layout

| Area | Project(s) |
|------|------------|
| CLI | `SharpClaw.Code.Cli`, `SharpClaw.Code.Commands` |
| Core contracts | `SharpClaw.Code.Protocol` |
| Runtime & orchestration | `SharpClaw.Code.Runtime` |
| Agents | `SharpClaw.Code.Agents` |
| Tools & permissions | `SharpClaw.Code.Tools`, `SharpClaw.Code.Permissions` |
| Providers, MCP, plugins | `SharpClaw.Code.Providers`, `SharpClaw.Code.Mcp`, `SharpClaw.Code.Plugins` |
| Sessions, telemetry, infra | `SharpClaw.Code.Sessions`, `SharpClaw.Code.Telemetry`, `SharpClaw.Code.Infrastructure` |

Full dependency and responsibility boundaries: [docs/architecture.md](docs/architecture.md), [AGENTS.md](AGENTS.md).

## Global CLI options

| Option | Description |
|--------|-------------|
| `--cwd <path>` | Working directory (default: current directory) |
| `--model <id>` | Model id / alias (`provider/model` forms supported where configured) |
| `--permission-mode <mode>` | `readOnly`, `workspaceWrite`, or `dangerFullAccess` (see [docs/permissions.md](docs/permissions.md)) |
| `--output-format text\|json` | Human vs structured output |

Subcommands include: `prompt`, `repl`, `doctor`, `status`, `session`, `mcp`, `plugins`, `version`, and more.

## Documentation

| Doc | Topic |
|-----|--------|
| [docs/architecture.md](docs/architecture.md) | Solution layout, dependencies, flows |
| [docs/runtime.md](docs/runtime.md) | Turns, `ConversationRuntime`, coordinators |
| [docs/sessions.md](docs/sessions.md) | Session snapshot + append-only event log |
| [docs/providers.md](docs/providers.md) | `IModelProvider`, configuration |
| [docs/tools.md](docs/tools.md) | Registry, executor, built-ins |
| [docs/permissions.md](docs/permissions.md) | Modes, rules, approvals |
| [docs/agents.md](docs/agents.md) | Agent Framework bridge |
| [docs/mcp.md](docs/mcp.md) | MCP registry and lifecycle |
| [docs/plugins.md](docs/plugins.md) | Plugins, trust, CLI |
| [docs/testing.md](docs/testing.md) | Unit, integration, parity harness |
| [ARCHITECTURE-NOTES.md](ARCHITECTURE-NOTES.md) | Architectural follow-ups |

## Contributing

Use **.NET 10** and **C# 13**. Follow [AGENTS.md](AGENTS.md) for project boundaries, JSON (`System.Text.Json` only), logging, and cross-platform behavior. PRs should build and tests should pass:

```bash
dotnet build SharpClawCode.sln
dotnet test SharpClawCode.sln
```

---

**Organization:** [github.com/clawdotnet](https://github.com/clawdotnet) · **Repository:** [github.com/clawdotnet/SharpClawCode](https://github.com/clawdotnet/SharpClawCode)
