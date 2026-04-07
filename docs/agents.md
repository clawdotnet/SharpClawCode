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

**`AgentRunContext`** includes **`IToolExecutor`**, but **`AgentFrameworkBridge`** does **not** currently register SharpClaw tools with the Microsoft Agent Framework tool-calling pipeline; **`AgentRunResult.ToolResults`** is an empty array in that bridge.

Tools remain available to **`DefaultTurnRunner`** consumers if a different agent implementation uses **`IToolExecutor`**; tests and parity harness exercise **`IToolExecutor`** directly.

## Metadata

**`ProviderBackedAgentKernel`** reads optional **`Metadata`** keys such as **`provider`** for provider name; **`DefaultTurnRunner`** puts resolved model info into **`AgentRunContext`** / assembler metadata (e.g. **`model`**).

For mock/parity scenarios, **`DeterministicMockModelProvider`** reads **`ParityMetadataKeys.Scenario`** in request metadata.
