# Snapshot edits, automatic compaction, and live turns

These features extend the existing runtime. The `read_file` and `edit_file` contracts, permission modes, checkpoint formats, and command result envelopes remain unchanged.

## Snapshot-aware editing

`read_file_snapshot` returns a bounded line window and a SHA-256 `snapshotId` for the complete decoded file text. It reads the content and computes the identifier from the same snapshot. Defaults are `startLine: 1` and `limit: 200`; the maximum limit is 500 lines. `nextLine` identifies the next window, or is null at EOF. Reading another window does not change the identifier unless the file changes.

```json
{"path":"src/Example.cs","startLine":1,"limit":100}
```

Pass that identifier to `edit_file_snapshot`:

```json
{
  "path": "src/Example.cs",
  "snapshotId": "<snapshotId returned by read_file_snapshot>",
  "edits": [
    {"startLine": 3, "deleteLineCount": 1, "newText": "    return result;\n"},
    {"startLine": 8, "deleteLineCount": 0, "newText": "// Additional explanation\n"}
  ]
}
```

Every line number addresses the original snapshot, even when earlier edits insert or remove lines. An edit replaces complete lines, including their terminators. `newText` supplies the exact replacement and any needed LF or CRLF terminators. Zero deleted lines inserts before `startLine`; `totalLineCount + 1` inserts at EOF. An empty file accepts insertion at line 1. Edits must not overlap or share an insertion point. A request accepts 1–100 edits to one file.

The entire batch is validated before writing. The tool stages a complete replacement in the same directory, checks the original content again immediately before publication, and records one mutation with before/after text for existing checkpoint undo/redo. Failures return a structured status such as `staleSnapshot`, `invalidEdits`, or `notFound`. A stale snapshot requires a fresh read. Unchanged batches produce no mutation.

Execution uses the normal `FileSystemWrite` policy and workspace resolver. Read-only and plan modes still block mutation. Snapshot writes share the semantic-write workspace lock with Roslyn rename. The final file replacement avoids partially written targets on cancellation; temporary files are cleaned up. Cancellation cannot interrupt the synchronous publication-to-journal-recording interval.

The lock coordinates cooperating writers, not external editors. There remains a filesystem race between the last content check and replacement; this is not an operating-system compare-and-swap. Like existing mutation snapshots, identifiers describe decoded text and writes use UTF-8, not byte-for-byte encoding/BOM preservation. Untouched line endings and Unix file modes are preserved.

## Durable automatic compaction

The runtime registers `IAgentContextManager`. The agent kernel invokes it before provider requests, including requests following completed tool exchanges. It accounts for system instructions, tool schemas, and reserved output when deciding whether maintenance is needed. It does not split a tool call from its result.

When needed, the selected provider receives a separate tool-free summarization request. Its validated JSON working state contains the objective, constraints, decisions, changed files, verification evidence, and outstanding work. Recent messages, the active user request, and pinned instruction/image messages remain verbatim. Summaries are conversation data and cannot grant permissions.

A `contextCompacted` event stores the working state, archived messages, replacement messages, token estimates, model, and measured summary usage. The event is persisted **before** the replacement is used. Original events are never deleted. Completed primary-turn snapshots participate in history replay, so a fresh host can resume from the same summary and retained messages. Child snapshots do not replace the parent's history. The turn's reported usage includes successful provider iterations and summary calls, including responses superseded by steering.

Configure the standard .NET configuration stack under `SharpClaw:Compaction` (or use `services.Configure<AutomaticCompactionOptions>`):

```json
{
  "SharpClaw": {
    "Compaction": {
      "Enabled": true,
      "ContextWindowTokens": 128000,
      "ReservedOutputTokens": 16384,
      "TriggerRatio": 0.8,
      "SummaryMaxTokens": 4096,
      "RetainedMessageCount": 6,
      "ModelContextWindows": {
        "provider/model": 128000
      }
    }
  }
}
```

Replace `provider/model` with the exact resolved provider/model key. The output reserve is increased when a request's maximum output is higher. Budgeting uses estimates (approximately characters divided by four, with an image allowance), not a provider tokenizer; configure conservative windows for your models.

Invalid summaries, persistence failures, or oversized irreducible requests fail visibly instead of silently dropping context. The original history remains intact. A single oversized request/exchange or an archive larger than the summarizer's window requires smaller tool outputs or a larger configured model window. This first implementation does not recursively summarize oversized archives. Automatic maintenance does not change the existing manual `/compact` command.

## Live turn control

The REPL displays tool activity and elapsed time while a prompt is running. Tool progress is suppressed for JSON output; existing result envelopes remain unchanged.

- Ordinary input during a turn queues a follow-up (maximum 16). It runs after the current turn settles, in the existing session.
- `/steer <text>` queues a correction for the next safe provider-request boundary. It does not interrupt an in-flight tool. A correction arriving during the final stream triggers another provider iteration, subject to the existing iteration limit.
- `/cancel` or Ctrl+C during a turn cancels that turn and its pending approvals while keeping the REPL available. Recorded edits remain available through normal recovery/undo.
- `/approve <id> yes` or `/approve <id> no` responds to the exact displayed approval request. Plain `yes`, queued prompts, and stale IDs cannot grant permission.
- `/exit` cancels the active turn, waits for cleanup, discards queued work, and exits. Ctrl+C while idle exits.
- EOF denies pending and future REPL approvals and allows remaining work to settle. It cannot leave an approval waiting for unavailable input.

The REPL remains available after a failed prompt. Its exit code reflects the last completed command/turn, so a provider or verification failure followed by EOF still exits nonzero.

Steering has a bounded queue (16 messages, at most 16,000 characters each). Accepted steering is persisted as `turnSteered` events and replayed with the conversation, including after cancellation or failure. Verification repairs also retain accepted corrections. Corrections that miss the final boundary remain queued as follow-up work. The REPL owns one terminal input read at a time; its approval transport never starts a competing console read. Other slash commands run while idle. Live controls currently wrap ordinary prompt turns; slash-command handlers retain their existing execution behavior.

Embedded callers can use the same control service without the terminal:

```csharp
using var turn = liveTurnControl.Begin(cancellationToken);
var execution = runtime.RunPromptAsync(request, turn.CancellationToken);

// From UI callbacks while execution is pending:
turn.TrySteer("Keep the existing public API unchanged.");
// turn.Cancel();

var result = await execution;
var pendingCorrections = turn.CloseSteering();
```

After execution settles, `CloseSteering()` atomically stops intake and returns pending corrections for the caller to retain as follow-ups. Read `turn.ReadEventsAsync(...)` concurrently for progress. Create one scope per invocation, pass its token through execution, and dispose it after execution settles. Scopes follow the caller's async flow; independent scopes do not broadcast each other's events or cancellations. Live progress is bounded and may drop old display events for slow readers; persisted session events remain authoritative. Steering never bypasses permission evaluation.

## Verification

Regression coverage includes stale and overlapping edits, CRLF/Unicode/EOF handling, permission denial, staged-write cancellation, durable undo/redo after restart, safe tool-pair compaction, invalid summaries and storage failures, fresh-host replay, mid-turn compaction, live steering, cancellation/resume, concurrent scope isolation, queued REPL input, exact-ID approval responses, and EOF during approval.
