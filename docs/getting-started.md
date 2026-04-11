# Getting Started with SharpClaw Code

Run a .NET-native coding agent in 15 minutes.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A terminal or command prompt
- A text editor (optional, for configuration)

## Clone and Build

Clone the repository and build the solution:

```bash
git clone https://github.com/clawdotnet/SharpClawCode.git
cd SharpClawCode
dotnet build SharpClawCode.sln
```

Run the test suite to verify your build:

```bash
dotnet test SharpClawCode.sln
```

All tests should pass. If they don't, check that you have .NET 10 SDK installed:

```bash
dotnet --version
```

## Run the CLI

The CLI is in `src/SharpClaw.Code.Cli`. Start the interactive REPL:

```bash
dotnet run --project src/SharpClaw.Code.Cli
```

You'll see a prompt and command-line interface. This is the REPL.

## Interactive REPL

The REPL is your primary interface for chatting with the agent.

### Slash Commands

Type `/` to see available commands:

- `/help` – Show all available commands
- `/status` – Display current session and workspace state
- `/doctor` – Check runtime health and provider configuration
- `/session` – View or manage the current session
- `/mode` – Switch workflow mode (build, plan, spec)
- `/editor` – Open current conversation in $EDITOR
- `/export` – Export session history as JSON
- `/undo` – Undo the last turn
- `/redo` – Redo the last undone turn
- `/version` – Show SharpClaw version
- `/commands` – List custom workspace commands
- `/exit` – Exit the REPL

### Workflow Modes

The runtime supports three primary modes:

| Mode | Purpose |
|------|---------|
| `build` | Normal coding-agent execution; all tools enabled |
| `plan` | Analysis-first mode; planning tools only, no file/shell mutations |
| `spec` | Generate structured spec artifacts in `docs/superpowers/specs/` |

Switch modes in the REPL with `/mode build`, `/mode plan`, or `/mode spec`.

## Your First Prompt

Run a one-shot prompt without entering the REPL:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- prompt "List all .cs files in this workspace"
```

The agent will execute and print the result to stdout.

### Output Formats

Emit JSON instead of human-readable output:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- --output-format json prompt "Summarize the README"
```

Supported formats: `text` (default), `json`, `markdown`.

## Configuration

### API Keys (Environment Variables)

Set provider API keys before running the CLI:

```bash
export SHARPCLAW_ANTHROPIC_API_KEY=sk-ant-...
dotnet run --project src/SharpClaw.Code.Cli
```

Supported environment variables:

- `SHARPCLAW_ANTHROPIC_API_KEY` – Anthropic API key
- `SHARPCLAW_OPENAI_API_KEY` – OpenAI API key

### Configuration File

Alternatively, configure providers in `appsettings.json` (or `.local`):

```json
{
  "SharpClaw": {
    "Providers": {
      "Anthropic": {
        "ApiKey": "sk-ant-...",
        "Model": "claude-3-5-sonnet-20241022"
      },
      "OpenAI": {
        "ApiKey": "sk-...",
        "Model": "gpt-4-turbo"
      }
    }
  }
}
```

The runtime loads from:
1. Environment variables (highest priority)
2. `appsettings.{Environment}.json` (if applicable)
3. `appsettings.json` (default)
4. `appsettings.local.json` (if present)

## Embed in Your Own App

Use SharpClaw as a library in your .NET application.

### 1. Install the NuGet Package

```bash
dotnet add package SharpClaw.Code.Runtime
```

### 2. Register the Runtime

In your application startup, add SharpClaw to the dependency injection container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

var builder = Host.CreateApplicationBuilder(args);

// Add SharpClaw runtime
builder.Services.AddSharpClawRuntime(builder.Configuration);

var host = builder.Build();
```

### 3. Execute a Prompt

```csharp
using var host = builder.Build();
await host.StartAsync();

var runtime = host.Services.GetRequiredService<IConversationRuntime>();

var request = new RunPromptRequest(
    Prompt: "Analyze the current workspace",
    SessionId: null,                                  // new session
    WorkingDirectory: Environment.CurrentDirectory,
    PermissionMode: PermissionMode.Auto,
    OutputFormat: OutputFormat.Markdown,
    Metadata: new Dictionary<string, string> 
    { 
        { "user-id", "developer-1" }
    }
);

var result = await runtime.RunPromptAsync(request, CancellationToken.None);

Console.WriteLine(result.FinalOutput);
Console.WriteLine($"Session: {result.Session.Id}");
```

### 4. Reuse Sessions

Sessions are durable. Resume an existing session by passing `SessionId`:

```csharp
var latestSession = await runtime.GetLatestSessionAsync(
    workspacePath: Environment.CurrentDirectory,
    cancellationToken: CancellationToken.None
);

var request = new RunPromptRequest(
    Prompt: "Continue from before",
    SessionId: latestSession?.Id,  // Resume this session
    WorkingDirectory: Environment.CurrentDirectory,
    PermissionMode: PermissionMode.Auto,
    OutputFormat: OutputFormat.Markdown,
    Metadata: null
);

var result = await runtime.RunPromptAsync(request, CancellationToken.None);
```

### Minimal Example

Complete console app:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSharpClawRuntime(builder.Configuration);

var host = builder.Build();
await host.StartAsync();

try
{
    var runtime = host.Services.GetRequiredService<IConversationRuntime>();
    var result = await runtime.RunPromptAsync(
        new RunPromptRequest(
            "What is in this directory?",
            SessionId: null,
            WorkingDirectory: Environment.CurrentDirectory,
            PermissionMode: PermissionMode.Auto,
            OutputFormat: OutputFormat.Markdown,
            Metadata: null
        ),
        CancellationToken.None
    );

    Console.WriteLine(result.FinalOutput);
}
finally
{
    await host.StopAsync();
}
```

## Next Steps

Learn more about SharpClaw:

- **[Architecture](architecture.md)** – Design, layers, and runtime model
- **[Sessions](sessions.md)** – Durable state, history, checkpoints, and recovery
- **[Tools](tools.md)** – Available tools and integration patterns
- **[Providers](providers.md)** – Provider abstraction, Anthropic, OpenAI, and custom backends
- **[MCP Support](mcp.md)** – Model Context Protocol servers and lifecycle
- **[Agents](agents.md)** – Agent Framework integration and configuration
- **[Runtime Concepts](runtime.md)** – Execution model, turns, events, and telemetry
- **[Permissions](permissions.md)** – Permission modes and approval gates
- **[Testing](testing.md)** – Unit and integration testing strategies
- **[Plugins](plugins.md)** – Extending SharpClaw with custom plugins

## Troubleshooting

### Agent doesn't respond or times out

Check that your API key is set:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- doctor
```

Look for your provider (Anthropic or OpenAI) in the output. If it shows "not configured", set `SHARPCLAW_ANTHROPIC_API_KEY` or configure `appsettings.json`.

### Build fails with .NET version error

Ensure you have .NET 10:

```bash
dotnet --version
```

If you have an older version, [install .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

### REPL commands not available

Update to the latest main branch:

```bash
git pull origin main
dotnet build SharpClawCode.sln
```

### Tests fail

Run with verbose output:

```bash
dotnet test SharpClawCode.sln --verbosity detailed
```

Check that all prerequisites are installed and your internet connection is stable (tests may fetch test fixtures or run integration tests).

## Questions?

- Open an issue: [github.com/clawdotnet/SharpClawCode/issues](https://github.com/clawdotnet/SharpClawCode/issues)
- Read the [README](../README.md) for a full feature overview
