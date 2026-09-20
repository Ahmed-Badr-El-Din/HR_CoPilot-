# Issue 012 — Evaluation harness (FR-3)

- **Type:** feature · **Milestone:** M4 · **Status:** open
- **Scope:** Golden set ≥25 Q/A + ≥5 adversarial (out-of-corpus, ambiguous, conflicting
  sources) + ≥3 prompt-injection cases (direct and indirect via ingested documents), with
  Arabic + English + cross-lingual cases. Runnable harness reporting retrieval hit-rate,
  groundedness, refusal correctness, bias-audit pass rate — Arabic metrics reported
  **separately**. Baseline numbers recorded including failures.
- **Why:** "The requirement that separates shipped from demoed RAG"; T1 Arabic metrics.
- **Acceptance:** `dotnet run --project HR.Evaluation` prints per-category metrics; results
  mirrored into `docs/EVALUATION.md`.