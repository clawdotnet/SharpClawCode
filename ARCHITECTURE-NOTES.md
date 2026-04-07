# Architecture conformance — follow-ups

High-effort or cross-cutting items identified during review. Low/medium fixes are applied in code; these remain intentional backlog.

## Runtime command surface vs. domain handlers

- **TODO:** `SharpClaw.Code.Commands` references `Mcp` and `Plugins` directly for `mcp` and `plugins` CLI commands. Consider adding narrow application interfaces (for example extending `IRuntimeCommandService` or introducing workspace-admin command services) so Commands depends on abstractions and the runtime layer owns orchestration policy, matching AGENTS.md dependency guidance.

## Stable JSON for MCP diagnostics

- **TODO:** `McpDoctorService` builds `DataJson` with anonymous objects and reflection-based `JsonSerializer.Serialize`. Align with `ProtocolJsonContext` by introducing explicit DTOs (for example `McpWorkspaceStatusReport`, `McpDoctorReport`) in `SharpClaw.Code.Protocol` and source-generated serialization, similar to operational reports.

## Plugin manifest parsing

- **TODO:** `PluginsCommandHandler.LoadInstallRequestAsync` deserializes `PluginManifest` with `JsonSerializerDefaults.Web`. Move manifest DTOs into Protocol (or a shared manifest contract) and serialize/deserialize via `ProtocolJsonContext` for consistent naming, versioning, and CI-tested shapes.

## Telemetry sinks

- **TODO:** `IRuntimeEventPublisher` is event-first and uses `RuntimeEvent` from Protocol (good). Next step for production telemetry: optional exporters (structured logs correlation id, OpenTelemetry activities, file/ndjson trace sink) without turning the publisher into a string-first logger.

## Runtime ↔ Agents coupling

- **TODO:** `SharpClaw.Code.Runtime` references `SharpClaw.Code.Agents` directly. Acceptable while Agents is the sole orchestration adapter; if multiple agent backends appear, introduce a small `IConversationAgentRuntime` seam in Runtime implemented only by Agents to avoid framework types crossing the boundary.

## Tool registry caching (optional)

- **TODO:** `IToolRegistry` now resolves plugin tools asynchronously per call. If plugin enumeration becomes expensive, add a short-lived workspace-scoped cache with explicit invalidation on plugin state changes.
