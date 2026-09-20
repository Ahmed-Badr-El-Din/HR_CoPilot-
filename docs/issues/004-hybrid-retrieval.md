# Issue 004 — Hybrid retrieval with citations + refusal (FR-2)

- **Type:** feature · **Milestone:** M1 · **Status:** open
- **Scope:** Dense (pgvector/vector store port) + keyword (BM25, script-aware tokeniser)
  fused by Reciprocal Rank Fusion; metadata filtering by language/role; one justified
  enhancement = cross-lingual query expansion + metadata filtering (ADR-0004); structured
  citations to exact chunk; refusal on low evidence (deterministic signal-based threshold).
- **Why:** Core RAG contract; T1 cross-lingual behaviour.
- **Acceptance:** known-query integration tests return the expected chunk first; English
  question finds Arabic evidence and vice versa; low-evidence question refuses.