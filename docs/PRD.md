# SharpClaw Code — Product Requirements Document

## Document Info

| Field | Value |
|-------|-------|
| Status | Draft |
| Author | telli |
| Date | 2026-04-10 |
| Version | 1.0 |

---

## 1. Vision

SharpClaw Code is the production-grade, open-source .NET runtime for building and operating AI coding agents. Built on Microsoft Agent Framework, it adds the durability, permission, and operational layers that turn agent prototypes into shippable products.

**One-liner:** The production runtime that makes Microsoft Agent Framework real for coding agent workloads.

---

## 2. Strategic Positioning

### Phase 1 — The Production .NET Agent Runtime (Months 0-12)

**Beachhead:** .NET developers adopting Microsoft Agent Framework who need production-grade runtime capabilities beyond what the framework provides out of the box.

**Positioning:** "SharpClaw Code is the production runtime built on Microsoft Agent Framework. The framework gives you agent abstractions and provider integration — SharpClaw adds durable sessions, permission enforcement, MCP lifecycle management, structured telemetry, and an operational CLI surface. Together, they're what you need to ship a real coding agent."

**Relationship to the Microsoft ecosystem:**

SharpClaw Code is a **complement to Microsoft Agent Framework**, not a competitor. The relationship is analogous to how ASP.NET Core provides the web framework and tools like MassTransit or Wolverine add production messaging patterns on top.

| Layer | Microsoft Provides | SharpClaw Adds |
|-------|-------------------|----------------|
| Agent abstractions | Agent kernel, activity protocol | Coding-agent-specific orchestration (turns, context assembly) |
| Provider integration | Multi-provider interfaces | Provider resilience, auth preflight, streaming adapters |
| Tool execution | Tool invocation primitives | Permission policy engine, approval gates, workspace boundaries |
| Session state | In-memory by default | Durable snapshots, append-only event logs, checkpoints, undo/redo |
| MCP | — | First-class registration, supervision, health checks, lifecycle state |
| Plugin system | — | Manifest-based discovery, trust levels, out-of-process execution |
| Telemetry | Standard .NET logging | Structured event-first ring buffer, JSON export, usage tracking |
| CLI surface | — | Full REPL, slash commands, spec mode, JSON output |
| Testing | — | Deterministic mock provider, parity harness, named scenarios |

**Why Microsoft should care:** SharpClaw is one of the most complete open-source projects built on Agent Framework. It demonstrates that the framework is production-viable for complex workloads, drives NuGet downloads of `Microsoft.Agents.AI`, and provides a reference architecture that other teams can learn from.

**Co-marketing opportunities:**
- Featured in Microsoft Agent Framework documentation as a reference implementation
- Joint blog posts: "Building a Production Coding Agent with Microsoft Agent Framework and SharpClaw"
- .NET Conf / Build talk: "From Agent Framework to Production — Lessons from SharpClaw Code"
- Listed in the Agent Framework ecosystem / community projects page
- Collaboration on Agent Framework feature requests informed by real-world SharpClaw usage

### Phase 2 — Build Your Own Coding Agent (Months 6-18)

**Expand to:** Startups and ISVs building coding agent products who need an embeddable runtime on the Microsoft stack.

**Positioning:** "Ship your AI coding assistant in weeks, not months. SharpClaw Code brings Microsoft Agent Framework to production — you provide the experience."

**New capabilities required:**
- Embeddable runtime SDK (no CLI dependency)
- Multi-tenant session isolation
- Custom tool SDK with packaging and distribution
- White-label provider configuration
- Webhooks and event streaming for external integrations

**Microsoft alignment:** Position SharpClaw as the go-to path for ISVs adopting Agent Framework. Enterprise customers already on Azure and .NET get a runtime that fits their existing stack, identity, and compliance infrastructure.

### Phase 3 — The Open-Source Coding Agent for .NET Teams (Months 12-24)

**Expand to:** Individual .NET developers who want a local coding agent built on familiar Microsoft technologies.

**Positioning:** "A coding agent built on the Microsoft stack, for the Microsoft stack. Open-source, runs locally, respects your workspace."

**New capabilities required:**
- Polished interactive CLI experience (auto-complete, rich rendering)
- IDE integrations (VS Code extension, Rider plugin)
- Local model support (Ollama, llama.cpp via OpenAI-compatible provider)
- Workspace indexing and semantic code search
- Conversation memory across sessions

**Microsoft alignment:** The consumer agent demonstrates that Agent Framework powers real end-user experiences, not just enterprise backends. Opportunity for joint promotion as "the open-source coding agent for the .NET ecosystem."

---

## 3. Target Users

### Phase 1 Users

| Persona | Description | Pain Point |
|---------|-------------|------------|
| **Platform Engineer** | Building internal AI developer tools at a mid-to-large .NET shop | Agent Framework provides abstractions but not sessions, permissions, or audit — building that from scratch |
| **.NET Tech Lead** | Evaluating how to take Agent Framework to production | Needs durability, testing, observability — production characteristics beyond the framework primitives |
| **DevTools Startup Founder** | Building an AI-powered code review / generation tool on the Microsoft stack | Needs a runtime on top of Agent Framework so they can focus on their product, not infrastructure |
| **Microsoft Agent Framework Team** | Growing the Agent Framework ecosystem | Needs visible, high-quality projects that demonstrate the framework's production viability |

### Phase 2 Users

| Persona | Description | Pain Point |
|---------|-------------|------------|
| **ISV Product Manager** | Shipping a coding agent as part of a larger product | Needs embeddable runtime with multi-tenancy, not a CLI tool |
| **Enterprise Architect** | Standardizing agent infrastructure across teams | Needs governance, audit trails, and permission enforcement at scale |

### Phase 3 Users

| Persona | Description | Pain Point |
|---------|-------------|------------|
| **.NET Developer** | Wants a local coding agent that works well with C# / .NET projects | Existing agents (Claude Code, Cursor) are JS/Python-centric; poor .NET experience |
| **Open-Source Contributor** | Wants to build and extend a coding agent they can understand and modify | Closed-source agents can't be customized; Python agents aren't their stack |

---

## 4. Phase 1 Requirements

### 4.1 Core Runtime (Exists)

The following are implemented and tested:

- [x] Durable sessions with append-only event logs and JSON snapshots
- [x] Permission policy engine with workspace boundary enforcement
- [x] Anthropic and OpenAI-compatible provider abstraction
- [x] MCP server registration, supervision, and lifecycle management
- [x] Plugin discovery, manifest validation, and trust-based execution
- [x] Structured telemetry with ring buffer and JSON export
- [x] CLI with REPL, slash commands, and JSON output mode
- [x] Checkpoint-based undo/redo with mutation tracking
- [x] Spec workflow mode for structured requirements generation
- [x] Cross-platform support with Windows-safe behavior

### 4.2 Phase 1 Gaps (Must Build)

#### 4.2.1 Tool-Calling Loop in Agent Framework Bridge

**Priority:** P0
**Why:** The current agent bridge streams provider responses but does not execute tools within the agent loop. This is the single biggest functional gap — without it, SharpClaw Code is a streaming wrapper, not a coding agent.

**Requirements:**
- Agent receives tool-use requests from the provider response
- Agent dispatches tool calls through the existing IToolExecutor (inheriting permission checks)
- Tool results are fed back to the provider for the next turn iteration
- Multi-turn tool loops terminate on provider completion or configurable max iterations
- Each tool call is recorded as a runtime event (ToolStartedEvent, ToolCompletedEvent)

#### 4.2.2 Conversation History

**Priority:** P0
**Why:** Multi-turn conversations require prior context. Currently each prompt is stateless within the provider call.

**Requirements:**
- Session-scoped conversation history assembled from persisted events
- Configurable context window management (truncation, summarization)
- System prompt injection from workspace context (CLAUDE.md equivalent)
- History survives session resume

#### 4.2.3 NuGet Package Distribution

**Priority:** P1
**Why:** Adoption requires `dotnet add package`, not `git clone`.

**Requirements:**
- Publish core packages to NuGet.org:
  - `SharpClaw.Code.Protocol` — contracts only, zero dependencies
  - `SharpClaw.Code.Runtime` — full runtime with DI extensions
  - `SharpClaw.Code.Providers.Anthropic` — Anthropic provider
  - `SharpClaw.Code.Providers.OpenAi` — OpenAI-compatible provider
  - `SharpClaw.Code.Tools` — built-in tools and tool SDK
  - `SharpClaw.Code.Mcp` — MCP client integration
- Stable API surface with semantic versioning
- XML documentation included in packages

#### 4.2.4 Documentation and Getting Started

**Priority:** P1
**Why:** Framework adoption lives or dies on docs.

**Requirements:**
- Getting started guide: "Build your first agent in 15 minutes"
- Integration guide: "Using SharpClaw with Microsoft Agent Framework"
- Architecture deep-dive for contributors
- API reference generated from XML docs
- Example projects:
  - Minimal console agent
  - Web API agent with session persistence
  - MCP-enabled agent with custom tools

#### 4.2.5 CI/CD Pipeline

**Priority:** P1
**Why:** No CI currently exists. Contributors need confidence their PRs don't break things.

**Requirements:**
- GitHub Actions workflow: build + test on push/PR
- Matrix: ubuntu-latest, windows-latest, macos-latest
- NuGet package publishing on release tags
- Code coverage reporting

#### 4.2.6 Provider Resilience

**Priority:** P2
**Why:** Production workloads need retry logic, rate limiting, and graceful degradation.

**Requirements:**
- Configurable retry with exponential backoff for transient HTTP failures
- Rate limit detection and backoff (429 handling)
- Request timeout configuration per provider
- Circuit breaker pattern for repeated failures
- Fallback provider chain (try Anthropic, fall back to OpenAI)

#### 4.2.7 Observability

**Priority:** P2
**Why:** Production deployments need more than a ring buffer.

**Requirements:**
- OpenTelemetry activity/span integration for distributed tracing
- Structured log correlation IDs across turn execution
- Metrics: token usage, tool execution duration, provider latency
- Optional NDJSON trace file sink for offline analysis

### 4.3 Phase 1 Non-Goals

- IDE integrations (Phase 3)
- Multi-tenant session isolation (Phase 2)
- Local model hosting (Phase 3)
- Marketplace or plugin store (Phase 2)
- Billing or usage metering (Phase 2)
- GUI or web dashboard (Phase 2+)

---

## 5. Phase 2 Requirements (Summary)

| Capability | Description |
|------------|-------------|
| Embeddable Runtime SDK | Host SharpClaw runtime in ASP.NET Core, worker services, or custom hosts without the CLI |
| Multi-Tenant Sessions | Workspace and session isolation per tenant with configurable storage backends |
| Custom Tool SDK | Package, version, and distribute custom tools as NuGet packages with manifest metadata |
| Event Streaming | Webhooks or message bus integration for real-time event forwarding |
| Admin API | REST API for session management, provider configuration, and runtime health |
| Usage Metering | Token and tool usage tracking with per-tenant attribution |
| SSO / Auth Integration | Enterprise identity provider support for approval workflows |

---

## 6. Phase 3 Requirements (Summary)

| Capability | Description |
|------------|-------------|
| Polished CLI UX | Auto-complete, syntax highlighting, rich diff rendering, progress indicators |
| IDE Extensions | VS Code and JetBrains Rider extensions using ACP protocol |
| Local Models | Ollama and llama.cpp support via OpenAI-compatible provider |
| Workspace Indexing | Semantic code search, symbol navigation, dependency graph |
| Cross-Session Memory | Persistent memory that survives session boundaries (project knowledge, user preferences) |
| Community Plugin Registry | Discoverable, installable plugins from a public registry |

---

## 7. Success Metrics

### Phase 1 (Months 0-12)

| Metric | Target | Rationale |
|--------|--------|-----------|
| GitHub stars | 2,000 | Validates .NET community interest |
| NuGet downloads (monthly) | 5,000 | Measures actual adoption |
| Contributors (unique) | 25 | Healthy contributor ecosystem |
| Discord / community members | 500 | Engaged developer community |
| Production deployments (known) | 10 | Real-world validation |
| Documentation pages | 30+ | Comprehensive getting-started and reference |

### Phase 2 (Months 6-18)

| Metric | Target |
|--------|--------|
| Companies using embedded SDK | 5 |
| Enterprise pilot conversations | 10 |
| Monthly recurring revenue | $10K (from enterprise licenses) |

### Phase 3 (Months 12-24)

| Metric | Target |
|--------|--------|
| Monthly active CLI users | 5,000 |
| IDE extension installs | 2,000 |
| Community plugins published | 20 |

---

## 8. Technical Constraints

- **.NET 10 / C# 13** — non-negotiable; this is a .NET-native product
- **System.Text.Json only** — no Newtonsoft.Json; source-generated serialization
- **Cross-platform** — must work on Windows, macOS, and Linux
- **MIT license for core** — open-core model; proprietary extensions for enterprise
- **Protocol-first** — all contracts go through SharpClaw.Code.Protocol; no leaking provider types
- **Async end-to-end** — no sync-over-async or fire-and-forget patterns
- **Permission-aware by default** — tools must route through the policy engine

---

## 9. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| .NET coding agent market is too small | Medium | High | Phase 1 positions as production runtime layer (broader market), not just coding agent |
| Microsoft builds competing runtime layer into Agent Framework | Low-Medium | High | Stay close to the team; contribute upstream; position as community-driven complement rather than competitor. If Microsoft builds it, pivot to tooling/hosting layer on top |
| Agent Framework breaks API compatibility | Medium | Medium | Abstraction layer (AgentFrameworkBridge) already isolates framework types; pin to stable versions |
| Provider APIs change frequently | High | Low | Abstraction layer already isolates provider details |
| Contributor burnout (small team) | Medium | High | Keep scope tight per phase; automate CI/CD; accept contributions early |
| Enterprise sales cycle too long for Phase 2 | Medium | Medium | Offer self-serve enterprise tier alongside sales-led motion |
| Microsoft relationship doesn't materialize | Medium | Medium | Product stands alone regardless; Microsoft alignment is accelerant, not dependency |

---

## 10. Open Questions

1. Should the CLI be distributed as a `dotnet tool` (global install) or standalone binary?
2. What's the naming/branding strategy for paid tiers? ("SharpClaw Pro"? "SharpClaw Enterprise"?)
3. Should Phase 2 include a hosted/managed offering, or stay self-hosted only?
4. How deep should the Agent Framework integration go — should SharpClaw contribute features upstream, or keep the runtime layer cleanly separated?
5. Is there an opportunity to join Microsoft's Agent Framework partner/early-adopter program?
6. Should SharpClaw target inclusion in .NET project templates (e.g., `dotnet new sharpclaw-agent`)?
