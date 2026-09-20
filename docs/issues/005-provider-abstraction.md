# Issue 005 — Provider abstraction (FR-4/architecture)

- **Type:** feature · **Milestone:** M2 (agents) · **Status:** open
- **Scope:** One interface covering **completion, streaming, tool calling, embeddings**;
  ≥2 working adapters (OpenAI-compatible host + Ollama local) plus a deterministic `stub`
  for tests/eval-without-LLM; configuration-selected with a documented fallback chain,
  retry/backoff and per-call timeout; usage/token accounting hook.
- **Why:** Never need to pay; provider swap = config + adapter (clean architecture gate).
- **Acceptance:** changing `LLM__FallbackOrder` changes behaviour with zero code change;
  stub returns scripted, deterministic responses used by tests and the offline eval run.