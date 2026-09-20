# Issue 013 — Observability (FR-9)

- **Type:** feature · **Milestone:** M4 · **Status:** open
- **Scope:** Correlation ID request → orchestrator → agent → LLM call; per-request token &
  cost accounting persisted and queryable; custom trace store (clean alternative to
  OTel/self-hosted collector); health/readiness endpoints.
- **Why:** FR-9; everything observable.
- **Acceptance:** every LLM call records usage tied to a correlation ID; `/healthz` and
  `/readyz` reflect DB/provider state; a trace is replayable by run ID.