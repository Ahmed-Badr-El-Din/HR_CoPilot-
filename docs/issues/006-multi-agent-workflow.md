# Issue 006 — Multi-agent workflow (FR-4/FR-5)

- **Type:** feature · **Milestone:** M2 · **Status:** open
- **Scope:** Supervisor orchestrator + **Evidence Extractor**, **Rubric Scorer**,
  **Shortlist Drafter**; each agent: explicit role, restricted tool allow-list, typed
  contracts (already scaffolded in `HR.Domain/Screening`), termination condition. ≥4 tools,
  ≥1 write/side-effecting tool gated by approval. Orchestration controls: max-iteration
  breaker, per-step timeout, retry with backoff, graceful degradation to plain RAG.
- **Why:** The D6 workflow; excessive-agency control.
- **Acceptance:** unit tests with stubbed LLM for all agent paths; contract tests on tool
  schemas; a run is recordable step-by-step by run ID.