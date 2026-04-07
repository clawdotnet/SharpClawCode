# Deferred library migrations

This document records **explicitly deferred** migrations from the migration triage plan. These are intentionally **not implemented** until product or operational needs change.

## LibGit2Sharp (Git layer)

**Current state:** `GitWorkspaceService` and related flows use a testable `IProcessRunner` seam over the `git` CLI (see `SharpClaw.Code.Git` and `SharpClaw.Code.Infrastructure`).

**Why deferred:** Moving to LibGit2Sharp adds native `libgit2` packaging and cross-platform runtime cost without a clearly required capability beyond today’s read-oriented/diagnostic git usage.

**Revisit when:**

- SharpClaw needs materially richer git operations than prompt context and diagnostics (e.g. complex commits, graph manipulation, libgit2-only APIs).
- Shipping a `git` executable in deployment becomes an explicit product constraint.

**If revisited:** Treat as a dedicated migration with temp-repo fixtures and validation on macOS, Linux, and Windows.

## StreamJsonRpc (ACP stdio host)

**Current state:** `AcpStdioHost` implements the documented NDJSON wire contract in `docs/acp.md` with a small, explicit loop.

**Why deferred:** The ACP surface is still small (few methods). Migrating to StreamJsonRpc risks transport/framing mismatch without simplifying much business logic.

**Revisit when:**

- ACP grows beyond the current method surface, or bidirectional notifications/cancellation become significantly more complex.

**If revisited:** Add contract tests against the existing `AcpStdioHost` behavior first so NDJSON semantics stay locked before swapping transports.
