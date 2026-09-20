# Issue 016 — Phase 1: skill-driven hardening, domain completion and a real test suite (D6T1)

- **Type:** hardening · **Milestone:** M1 · **Status:** resolved by PR #10
- **Scope:** Phase 1 of the D6T1 evaluation agenda built on the `domain-architect`,
  `arabic-nlp-specialist` and `rag-engineer` skills: rename retrieval/chunking ports to the
  skill vocabulary (`IRetrievalService`, `IDocumentChunker`), introduce the `IArabicNormalizer`
  port + adapter, complete genuinely-missing domain entities (`JobRole`/`Competency`,
  `EvidenceChunk`, `AuditLog`), and stand up the first automated test suite in `HR.Tests`
  (idempotent ingestion, structure-preserving chunking, bilingual hybrid retrieval, Arabic
  normalization, synthetic corpus determinism, protected-attribute redaction).
- **Why:** `HR.Tests` had no test files (CI proved nothing), retrieval ports had skill names
  left unmapped, bilingual normalization was non-functional, and Screening lacked domain
  shapes and a bias-guard audit trail required by the D6 guard.
- **Acceptance:** `dotnet test HR.sln` green with ≥25 deterministic tests; `dotnet build
  HR.sln -warnaserror` and `dotnet format --verify-no-changes` green; a documented mapping of
  new ↔ existing domain shapes.