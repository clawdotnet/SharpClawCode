# Microsoft Agent Framework Integration

SharpClaw Code is built **on top of** the [Microsoft Agent Framework](https://github.com/microsoft/agents) (`Microsoft.Agents.AI` NuGet package). This guide explains how SharpClaw leverages the framework and what production capabilities SharpClaw adds.

## Overview

**Microsoft Agent Framework** provides:
- Abstract agent interfaces (`AIAgent`, `AgentSession`, `AgentResponse`)
- Session lifecycle management
- Chat message and tool-calling abstractions
- A foundation for building multi-turn agent systems

**SharpClaw Code** complements the framework by adding:
- Production-grade agent orchestration for coding tasks
- Provider abstraction layer with auth preflight and streaming adapters
- Permission-aware tool execution with approval gates
- Durable session snapshots and NDJSON event logs
- MCP (Model Context Protocol) server supervision
- Plugin system with trust levels and out-of-process execution
- Structured telemetry with ring buffer and usage tracking
- REPL and CLI with spec mode

## Quick comparison

| Layer | Agent Framework Provides | SharpClaw Adds |
|-------|--------------------------|---|
| **Agent abstractions** | `AIAgent`, `AgentSession`, `AgentResponse` | Coding-agent orchestration, turns, context assembly |
| **Provider integration** | Multi-provider interfaces | Resilience, auth preflight, streaming adapters, tool-use extraction |
| **Tool execution** | — | Permission-aware tools, approval gates, workspace boundaries |
| **Sessions** | In-memory | Durable snapshots, NDJSON event logs, checkpoints, undo/redo |
| **MCP support** | — | Server registration, supervision, health checks |
| **Plugins** | — | Manifest discovery, trust levels, out-of-process execution |
| **Telemetry** | Standard logging | Structured events, ring buffer, usage tracking |
| **CLI & REPL** | — | REPL, slash commands, JSON output, spec mode |

## Architecture

The integration is layered. Each layer builds on the one below:

```
Microsoft Agent Framework (AIAgent, AgentSession, AgentResponse)
    ↓
SharpClawFrameworkAgent (implements AIAgent)
    ↓
AgentFrameworkBridge (orchestration layer)
    ↓
ProviderBackedAgentKernel (provider + tool-calling loop)
    ↓
IModelProvider (Anthropic, OpenAI-compatible)
```

### Layer 1: SharpClawFrameworkAgent

**File:** `src/SharpClaw.Code.Agents/Internal/SharpClawFrameworkAgent.cs`

A concrete implementation of `AIAgent` that adapts SharpClaw's agent model to the framework:

```csharp
internal sealed class SharpClawFrameworkAgent(
    string agentId,
    string name,
    string description,
    Func<IEnumerable<ChatMessage>, AgentSession, AgentRunOptions, CancellationToken, Task<AgentResponse>> runAsync) 
    : AIAgent
```

Responsibilities:
- Provides framework-required properties (`Id`, `Name`, `Description`)
- Creates and deserializes `AgentSession` instances (backed by `SharpClawAgentSession`)
- Delegates core execution to a caller-provided delegate
- Implements streaming semantics by converting `AgentResponse` to `AgentResponseUpdate` sequences

The session state is serialized/deserialized via `StateBag`, allowing framework-level session persistence.

### Layer 2: AgentFrameworkBridge

**File:** `src/SharpClaw.Code.Agents/Services/AgentFrameworkBridge.cs`

The orchestration layer that:

1. **Translates context:** Converts `AgentFrameworkRequest` (SharpClaw's agent input model) into:
   - Tool registry entries → `ProviderToolDefinition` list
   - `ToolExecutionContext` (permissions, workspace bounds, mutation recorder)
   - Framework session and run options

2. **Instantiates the framework agent:** Creates a `SharpClawFrameworkAgent` with a delegate that calls `ProviderBackedAgentKernel`

3. **Orchestrates execution:** 
   ```csharp
   var frameworkAgent = new SharpClawFrameworkAgent(
       request.AgentId,
       request.Name,
       request.Description,
       async (messages, session, runOptions, ct) =>
       {
           providerResult = await providerBackedAgentKernel.ExecuteAsync(
               request,
               toolExecutionContext,
               providerTools,
               ct).ConfigureAwait(false);
           return new AgentResponse(new ChatMessage(ChatRole.Assistant, providerResult.Output));
       });
   
   response = await frameworkAgent.RunAsync(request.Context.Prompt, session, cancellationToken: cancellationToken);
   ```

4. **Returns an `AgentRunResult`** with:
   - Output text
   - Token usage metrics
   - Provider request/response details
   - Tool results and runtime events
   - `AgentSpawnedEvent` and `AgentCompletedEvent` for session telemetry

### Layer 3: ProviderBackedAgentKernel

**File:** `src/SharpClaw.Code.Agents/Internal/ProviderBackedAgentKernel.cs`

The core execution engine for streaming provider responses and driving the tool-calling loop.

Key responsibilities:

1. **Auth preflight:** Checks `IAuthFlowService` to verify the provider is authenticated before making calls
2. **Provider resolution:** Uses `IModelProviderResolver` to get the configured `IModelProvider`
3. **Message assembly:** Builds the conversation thread from:
   - System prompt
   - Prior turn history (multi-turn context)
   - Current user prompt
4. **Tool-calling loop:**
   - Calls `provider.StartStreamAsync()` to get a streaming provider event sequence
   - Extracts `ProviderEvent` items (text chunks, tool-use invocations, usage stats)
   - On tool-use events, constructs `ContentBlock` entries with tool name, ID, and input JSON
   - Dispatches each tool via `ToolCallDispatcher` (which runs through the permission engine)
   - Feeds tool results back to the provider in the next iteration
   - Repeats until max iterations or no tool calls remain

5. **Error handling:**
   - Missing provider → `ProviderExecutionException` with `ProviderFailureKind.MissingProvider`
   - Auth check failure → `ProviderFailureKind.AuthenticationUnavailable`
   - Stream error → `ProviderFailureKind.StreamFailed`
   - Placeholder response when stream is empty

**Loop Configuration:** Controlled by `AgentLoopOptions`:
- `MaxToolIterations` — maximum rounds (default 25)
- `MaxTokensPerRequest` — per-iteration token budget

### Layer 4: IModelProvider

**File:** `src/SharpClaw.Code.Providers/Abstractions/IModelProvider.cs`

SharpClaw's abstraction over model providers:

```csharp
public interface IModelProvider
{
    string ProviderName { get; }
    Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);
    Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken);
}
```

**Registered implementations:**
- `AnthropicProvider` — Anthropic Claude models via HTTP
- `OpenAiCompatibleProvider` — OpenAI-compatible endpoints (LM Studio, Ollama, etc.)

Both stream `ProviderEvent` sequences containing:
- Text chunks
- Tool-use invocations (`ToolUseId`, `ToolName`, `ToolInputJson`)
- Terminal usage metrics

## Integration entry points

### 1. Extending SharpClaw Agent Types

SharpClaw provides a base class for custom agents:

**File:** `src/SharpClaw.Code.Agents/Agents/SharpClawAgentBase.cs`

```csharp
public abstract class SharpClawAgentBase(IAgentFrameworkBridge agentFrameworkBridge) : ISharpClawAgent
{
    public abstract string AgentId { get; }
    public abstract string AgentKind { get; }
    protected abstract string Name { get; }
    protected abstract string Description { get; }
    protected abstract string Instructions { get; }

    public virtual Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        => agentFrameworkBridge.RunAsync(
            new AgentFrameworkRequest(
                AgentId,
                AgentKind,
                Name,
                Description,
                Instructions,
                context),
            cancellationToken);
}
```

**To add a custom agent:**

1. Inherit from `SharpClawAgentBase`
2. Provide concrete implementations of `AgentId`, `AgentKind`, `Name`, `Description`, `Instructions`
3. Optionally override `RunAsync` to customize behavior before/after framework execution
4. Register in DI:
   ```csharp
   services.AddSingleton<YourCustomAgent>();
   services.AddSingleton<ISharpClawAgent>(sp => sp.GetRequiredService<YourCustomAgent>());
   ```

**Example:** `PrimaryCodingAgent` (default agent for prompts):
```csharp
public sealed class PrimaryCodingAgent(IAgentFrameworkBridge agentFrameworkBridge) 
    : SharpClawAgentBase(agentFrameworkBridge)
{
    public override string AgentId => "primary-coding-agent";
    public override string AgentKind => "primaryCoding";
    protected override string Name => "Primary Coding Agent";
    protected override string Description => "Handles the default coding workflow for prompt execution.";
    protected override string Instructions => "You are SharpClaw Code's primary coding agent. ...";
}
```

### 2. Adding a Custom Model Provider

Implement `IModelProvider` to integrate a new model source:

```csharp
public sealed class YourModelProvider : IModelProvider
{
    public string ProviderName => "your-provider";

    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        // Check if credentials are available (API key, token, etc.)
        return new AuthStatus(IsAuthenticated: _hasCredentials);
    }

    public async Task<ProviderStreamHandle> StartStreamAsync(
        ProviderRequest request, 
        CancellationToken cancellationToken)
    {
        // Stream model responses as ProviderEvent sequences
        return new ProviderStreamHandle(
            Events: StreamEventsAsync(request, cancellationToken));
    }

    private async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
        ProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Emit text chunks as ProviderEvent with IsTerminal=false
        // Emit tool-use events with ToolUseId, ToolName, ToolInputJson
        // Emit usage metrics in the final ProviderEvent with IsTerminal=true
    }
}
```

**Registration:**

```csharp
public static void AddYourProvider(this IServiceCollection services)
{
    services.AddSingleton<IModelProvider, YourModelProvider>();
    // Configure options if needed
    services.Configure<YourModelProviderOptions>(configuration.GetSection("Your:Provider"));
}
```

**Provider catalog:** Update `ProviderCatalogOptions` to register aliases:
```json
{
  "SharpClaw:Providers:Catalog": {
    "DefaultProvider": "your-provider",
    "ModelAliases": {
      "default": "your-provider/latest-model"
    }
  }
}
```

### 3. Adding Custom Tools

Custom tools integrate via the registry and are automatically available to agents:

**File:** `src/SharpClaw.Code.Tools/Abstractions/ISharpClawTool.cs`

```csharp
public interface ISharpClawTool
{
    ToolDefinition Definition { get; }
    PluginToolSource? PluginSource { get; }
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken);
}
```

**Implementation:** Extend `SharpClawToolBase` for the common pattern:

```csharp
public sealed class YourCustomTool(IPathService pathService) : SharpClawToolBase
{
    public override ToolDefinition Definition { get; } = new(
        Name: "your-tool",
        Description: "Does something useful",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: "YourToolArguments",
        InputDescription: "JSON object with tool parameters.",
        Tags: ["custom"]);

    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<YourToolArguments>(request);
        // Perform work respecting context.WorkspaceRoot, context.PermissionMode, etc.
        return CreateSuccessResult(context, request, "output text", null);
    }
}
```

**DI Registration:**

```csharp
services.AddSingleton<ISharpClawTool, YourCustomTool>();
```

**Tool calling:** The agent kernel automatically:
1. Includes tool schemas in the initial provider request
2. Extracts tool-use events from the provider stream
3. Dispatches via `ToolCallDispatcher` (which consults the permission engine)
4. Collects results and feeds them back to the provider for continued reasoning

See [tools.md](tools.md) for full details on tool execution, permissions, and plugin integration.

## Tool-calling flow within the framework

The `ProviderBackedAgentKernel` drives a multi-iteration loop that respects the framework's abstractions:

1. **Iteration N:** Call `provider.StartStreamAsync()` with conversation history + tool schemas
2. **Stream processing:** Collect text and tool-use events
3. **Build assistant message:** Add text block and tool-use content blocks to conversation
4. **Tool dispatch:** Call `ToolCallDispatcher` for each tool-use event
5. **Build user message:** Add tool result content blocks
6. **Continue:** Append both messages and loop back to step 1
7. **Exit:** When iteration returns no tool-use events, break and return accumulated text

This pattern keeps the framework session state synchronized with the multi-turn conversation and tool results.

## Configuration and instantiation

### Runtime integration

The `ConversationRuntime` owns agent execution via the `DefaultTurnRunner`:

```
Prompt input
    ↓
ConversationRuntime.RunPromptAsync
    ↓
DefaultTurnRunner.RunAsync (assembles context)
    ↓
PrimaryCodingAgent.RunAsync (framework bridge)
    ↓
AgentFrameworkBridge.RunAsync
    ↓
ProviderBackedAgentKernel.ExecuteAsync (tool-calling loop)
    ↓
IModelProvider.StartStreamAsync (streaming)
```

### Service registration

The agents module registers via `AgentsServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddSharpClawAgents(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Register bridge, kernel, concrete agents, etc.
    services.AddSingleton<IAgentFrameworkBridge, AgentFrameworkBridge>();
    services.AddSingleton<ProviderBackedAgentKernel>();
    services.AddSingleton<PrimaryCodingAgent>();
    services.AddSingleton<ReviewerAgent>();
    // ... other agents
    
    services.Configure<AgentLoopOptions>(configuration.GetSection("SharpClaw:AgentLoop"));
    return services;
}
```

The CLI host calls:
```csharp
services.AddSharpClawRuntime(configuration);  // includes agents
```

## Key architectural decisions

### Why a bridge?

The `AgentFrameworkBridge` isolates **SharpClaw's agent orchestration** (context assembly, tool dispatch, permission checks) from **Microsoft Agent Framework's abstractions** (session, message, response). This allows:

- **Version independence:** Framework updates don't force refactoring across SharpClaw
- **Testing:** Bridge can be tested with mock providers and kernels
- **Clarity:** Clear contract between layers; framework details are hidden from callers

### Why ProviderBackedAgentKernel?

Separates **provider streaming** and **tool-calling logic** from **framework integration**:

- **Streaming:** Handles partial chunks, tool-use extraction, usage metrics
- **Tool calling:** Drives the multi-iteration loop, permission checks, result collection
- **Auth checks:** Runs preflight before expensive provider calls
- **Error handling:** Classifies failures and maps to `ProviderFailureKind`

This kernel can be tested independently or used in non-framework contexts (e.g., batch processing).

### Why IModelProvider over framework providers?

SharpClaw's `IModelProvider` is:

- **Simpler:** One async method to stream events
- **Resilient:** Built-in auth preflight and preflight normalization
- **Pluggable:** Easy to add new endpoints (Anthropic, OpenAI-compatible, custom)
- **Streaming-first:** Designed for partial updates and tool-calling loops

The framework provides `IChatCompletionService` and `IEmbeddingService` abstractions; SharpClaw adds `IModelProvider` for agent-specific streaming requirements.

## Testing

### Unit testing the bridge

Test with a mock provider:

```csharp
var mockProvider = new MockModelProvider();
services.AddSingleton<IModelProvider>(mockProvider);

var bridge = new AgentFrameworkBridge(/* deps */);
var result = await bridge.RunAsync(request, cancellationToken);

Assert.NotNull(result.Output);
Assert.Equal(request.AgentId, result.AgentId);
```

### Integration testing

Use the `SharpClaw.Code.MockProvider` test fixture:

```csharp
var host = TestHostBuilder.BuildWithMockProvider();
var runtime = host.Services.GetRequiredService<IConversationRuntime>();

var result = await runtime.RunPromptAsync(
    sessionId: "test-session",
    prompt: "What is 2 + 2?",
    cancellationToken);

Assert.Contains("4", result.Output);
```

See [testing.md](testing.md) for full test patterns.

## Further reading

- [Architecture](architecture.md) — Solution structure and overall data flow
- [Providers](providers.md) — Provider interface, registration, and catalog
- [Tools](tools.md) — Tool registry, execution, permissions, and plugins
- [Sessions](sessions.md) — Session snapshots, event logs, and checkpoints
- [MCP](mcp.md) — Model Context Protocol server registration and supervision
- [Testing](testing.md) — Test patterns and fixtures

## Microsoft Agent Framework links

- [Microsoft Agent Framework GitHub](https://github.com/microsoft/agents)
- [AIAgent interface documentation](https://github.com/microsoft/agents/blob/main/dotnet/src/Microsoft.Agents.Core/AIAgent.cs)
- [Agent Framework samples](https://github.com/microsoft/agents/tree/main/dotnet/samples)
