# AGENTS.md

## Project Identity

`SharpClaw Code` is a C#-native coding-agent harness inspired by the current Rust `claw-code` surface.

Primary stack:

- .NET 10
- C# 13
- Microsoft Agent Framework
- `System.CommandLine`
- `Spectre.Console`
- `System.Text.Json`
- `Microsoft.Extensions.Hosting`
- idiomatic Microsoft dependency injection

This repository should grow into a production-grade, cross-platform agent runtime with strong Windows compatibility. Favor concrete, maintainable architecture over framework-heavy indirection.

## Current Repository State

The repository may begin sparse or empty. Before implementing anything:

1. Inspect the current solution and folder layout.
2. Align new work to what already exists unless the user explicitly asks for restructuring.
3. If the intended architecture is not present yet, create the smallest real vertical slice that establishes it cleanly.

Do not scaffold broad placeholder trees with no executable value.

## Non-Negotiable Engineering Rules

- Target `.NET 10` and `C# 13`.
- Prefer clear, explicit, strongly typed code over clever abstractions.
- Use `async`/`await` end-to-end anywhere I/O or external process work exists.
- Use records for immutable contracts where appropriate.
- Use interfaces for infrastructure seams and runtime orchestration seams.
- Avoid unnecessary repository patterns and avoid overengineered CQRS.
- Keep domain and protocol contracts dependency-light.
- Use `System.Text.Json`, never `Newtonsoft.Json`.
- Use `Microsoft.Extensions.Logging` abstractions for logging.
- Prefer file-scoped namespaces.
- Favor small, testable classes with focused responsibilities.
- Preserve machine-readable state and JSON output support across major features.
- Route dangerous shell execution or file mutation through an explicit permission policy layer.
- Model long-running or lifecycle-managed subsystems explicitly and use hosted services where appropriate.
- Build for cross-platform support, with strong Windows behavior and path/process compatibility.
- Do not hardcode secrets, API keys, tokens, or machine-specific paths.
- Add concise XML docs for all public APIs and major internal services.
- Add `TODO` comments only when paired with a concrete reason.

## Architecture Direction

The solution is expected to contain these primary projects:

- `SharpClaw.Code.Cli`
- `SharpClaw.Code.Protocol`
- `SharpClaw.Code.Runtime`
- `SharpClaw.Code.Agents`
- `SharpClaw.Code.Commands`
- `SharpClaw.Code.Providers`
- `SharpClaw.Code.Tools`
- `SharpClaw.Code.Permissions`
- `SharpClaw.Code.Sessions`
- `SharpClaw.Code.Memory`
- `SharpClaw.Code.Git`
- `SharpClaw.Code.Web`
- `SharpClaw.Code.Mcp`
- `SharpClaw.Code.Plugins`
- `SharpClaw.Code.Skills`
- `SharpClaw.Code.Telemetry`
- `SharpClaw.Code.Infrastructure`

The solution should also include dedicated test coverage for:

- unit tests
- integration tests
- mock provider behavior
- parity harness coverage

If the exact project layout differs slightly in the live repository, follow the existing naming and folder conventions unless the user asks to normalize them.

## Project Responsibilities

Use these boundaries when deciding where code belongs:

- `SharpClaw.Code.Protocol`: central contracts, DTOs, envelopes, event shapes, request/response models, and dependency-light shared abstractions. Keep this project near the center of the dependency graph.
- `SharpClaw.Code.Cli`: command-line entrypoints, option binding, console wiring, startup composition. Keep it thin.
- `SharpClaw.Code.Commands`: application-facing command handlers and command orchestration invoked by the CLI.
- `SharpClaw.Code.Runtime`: turn execution, context assembly, lifecycle coordination, recovery, retries, and orchestration flow.
- `SharpClaw.Code.Agents`: Microsoft Agent Framework integration plus primary, reviewer, advisor, and sub-agent implementations.
- `SharpClaw.Code.Tools`: tool definitions and execution adapters. Tool execution must pass through `SharpClaw.Code.Permissions`.
- `SharpClaw.Code.Permissions`: policy evaluation, approvals, dangerous-action gates, and permission-aware execution decisions.
- `SharpClaw.Code.Sessions`: durable session state, snapshots, append-only event logs, resume/recovery data.
- `SharpClaw.Code.Memory`: memory extraction, indexing, recall, summarization boundaries, and retention policies.
- `SharpClaw.Code.Providers`: model/provider integrations and provider-specific runtime adapters.
- `SharpClaw.Code.Git`: git inspection and git-aware workspace operations.
- `SharpClaw.Code.Web`: web fetch, browser-facing integrations, and web-specific execution concerns.
- `SharpClaw.Code.Mcp`: MCP client integration, server registration, transport concerns, and typed lifecycle state.
- `SharpClaw.Code.Plugins`: plugin discovery, activation, manifest handling, and typed lifecycle state.
- `SharpClaw.Code.Skills`: skill discovery, loading, packaging, and execution metadata.
- `SharpClaw.Code.Telemetry`: structured, event-first telemetry contracts, sinks, correlation, and emission helpers.
- `SharpClaw.Code.Infrastructure`: implementation details that do not belong in the domain-oriented projects above.

## Dependency Guidelines

- Keep `Protocol` as central and minimally dependent as possible.
- `Cli` should delegate to `Commands` and `Runtime`, not absorb business logic.
- `Runtime` owns orchestration and lifecycle, not the CLI.
- `Agents` should integrate the Microsoft Agent Framework without leaking provider or transport details across the solution.
- `Tools` must not bypass permissions.
- `Sessions` must support both durable snapshots and append-only event logs.
- `Telemetry` should be structured and event-first, not string-first.
- MCP and plugin lifecycle states must be explicit, typed, and observable.

When in doubt, prefer adding a narrowly scoped service in the correct project over introducing a new cross-cutting abstraction.

## Code Style Expectations

- Prefer constructor injection over service location.
- Pass `CancellationToken` through async call paths where work may block, wait, stream, or invoke I/O.
- Keep public contracts explicit and version-tolerant where they may be serialized or persisted.
- Favor immutable options/configuration objects once bound.
- Keep JSON payloads and machine-readable outputs stable unless the user asks for breaking changes.
- Prefer named types over anonymous or weakly typed dictionaries for important runtime state.
- Use `enum` or discriminated-style typed models only when the state machine is truly finite and worth modeling explicitly.
- Keep file and class names aligned to their primary responsibility.

## Documentation Expectations

- Add concise XML docs to public APIs and major internal services.
- Document invariants, lifecycle expectations, and permission-sensitive behavior close to the code.
- Keep comments high-signal. Explain why when it is not obvious from the code.

## Delivery Rules For Future Codex Runs

- Inspect the current repository layout before implementing.
- For non-trivial tasks, write a short plan before making edits.
- Prefer incremental, reviewable changes over broad speculative scaffolding.
- After changes, run builds and tests for the affected scope and fix obvious issues.
- When adding files, keep the folder structure intentional and minimal.
- If a requested feature is large, implement the vertical slice that makes the architecture real instead of adding disconnected stubs.
- Preserve existing machine-readable output, JSON contracts, persistence formats, and permission behavior unless the task explicitly changes them.

## What To Avoid

- Do not introduce `Newtonsoft.Json`.
- Do not hide important runtime behavior behind clever abstractions.
- Do not add repository layers or CQRS patterns unless the user explicitly needs them.
- Do not let CLI code become the application core.
- Do not bypass permission checks for shell, file mutation, or similarly dangerous actions.
- Do not introduce fake completeness through placeholder projects, empty handlers, or dead extension points.

## Default Implementation Bias

For this repository, prefer:

- explicit orchestration over magic
- typed contracts over ad hoc blobs
- durable state over implicit in-memory assumptions
- hosted lifecycle services over unmanaged background threads
- cross-platform behavior with deliberate Windows-safe handling
- pragmatic architecture with clean seams over pattern collection
