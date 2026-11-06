# Agents

**Project:** `SharpClaw.Code.Agents`  
**Package:** `Microsoft.Agents.AI`  
**Registration:** `AgentsServiceCollectionExtensions.AddSharpClawAgents`

## Registered agents

Each agent is **`ISharpClawAgent`** and **`AddSingleton`**, also exposed as **`ISharpClawAgent`** via `GetRequiredService`:

- **`PrimaryCodingAgent`** — used by **`DefaultTurnRunner`**
- **`ReviewerAgent`**, **`AdvisorAgent`**, **`SecurityReviewAgent`**, **`SubAgentWorker`**, **`RecoveryAgent`**

All concrete agents inherit **`SharpClawAgentBase`**, which implements **`RunAsync(AgentRunContext, CancellationToken)`** by building **`AgentFrameworkRequest`** and calling **`IAgentFrameworkBridge.RunAsync`**.

## Framework bridge

**`AgentFrameworkBridge`**:

1. Constructs **`SharpClawFrameworkAgent`** (Microsoft Agent Framework) with a delegate that runs **`ProviderBackedAgentKernel.ExecuteAsync`**.
2. Creates a framework session and calls **`RunAsync`** with the user prompt.
3. On **`ProviderExecutionException`**, logs and rethrows.
4. Builds **`AgentRunResult`** with output, usage, provider data, and **`RuntimeEvent`** entries (**`AgentSpawnedEvent`**, **`AgentCompletedEvent`**).

## Provider kernel

**`ProviderBackedAgentKernel`** (internal):

- Runs **`IProviderRequestPreflight`**, **`IAuthFlowService`**, **`IModelProviderResolver`**, **`IModelProvider.StartStreamAsync`**
- Aggregates stream text into output; may return placeholder text when unauthenticated or empty stream (see kernel implementation for exact conditions)
- Throws **`ProviderExecutionException`** for missing provider, auth probe failure, or stream errors classified as **`ProviderFailureKind`**

## Tools and the agent path

**`AgentFrameworkBridge`** builds the permission-aware tool execution context and supplies registry definitions to **`ProviderBackedAgentKernel`**. The kernel owns the multi-iteration provider tool loop and uses **`ToolCallDispatcher`** to execute tools through **`IToolExecutor`**. The bridge returns the collected tool results in **`AgentRunResult.ToolResults`**.

At complete tool boundaries, the kernel accepts queued steering and invokes the optional **`IAgentContextManager`** registered by Runtime for durable automatic compaction. Neither path bypasses tool permissions. See [interactive runtime](interactive-runtime.md).

## Metadata

**`ProviderBackedAgentKernel`** reads optional **`Metadata`** keys such as **`provider`** for provider name; **`DefaultTurnRunner`** puts resolved model info into **`AgentRunContext`** / assembler metadata (e.g. **`model`**).

For mock/parity scenarios, **`DeterministicMockModelProvider`** reads **`ParityMetadataKeys.Scenario`** in request metadata.
