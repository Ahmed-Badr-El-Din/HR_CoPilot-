# Issue 010 — Real-time streaming + cancellation (FR-6)

- **Type:** feature · **Milestone:** M3 · **Status:** open
- **Scope:** SSE endpoints for token-level completion streaming and live agent/tool progress
  events; client cancellation that actually stops server-side work (shared
  CancellationToken into providers, tool loop and DB writes).
- **Why:** FR-6; "not a frozen spinner".
- **Acceptance:** an SSE client receives incremental tokens; cancelling a run terminates the
  provider call server-side and persists a `Cancelled` run state.