# Agentic workflow for this repository

This document describes how human and AI workflows are organised in the repo,
how the D6 product pipeline executes, and the conventions an agent (opencode or
sub-agent) must follow. `AGENTS.md` is the machine-readable binding contract;
this file is the readable explanation and index.

## 1. The product pipeline (D6 — talent screening)

The D6 variant is an agentic, bilingual HR screening copilot. One end-to-end flow:

```
Ingest documents (txt/md/json/pdf/docx)
        │  DocumentIngestionService + CompositeDocumentParser + StructureChunker
        ▼
HR.Domain documents + chunks (embeddings persisted)
        │
        ▼
Hybrid retrieval (dense + bilingual keyword, RRF)
        │  HybridRetriever — bilingual expansion via BilingualLexicon (T1)
        ▼
Multi-agent screening (ScreeningOrchestrator)
        ├─ EvidenceExtractorAgent  → redacts protected attributes, quotes grounded evidence
        ├─ RubricScorerAgent       → scores dimensions on redacted evidence only (RubricMath)
        └─ ShortlistDrafterAgent   → drafts shortlist + interview probes
        │
        ▼
Approval gate × audit trail (BiasAudits, AuditLogs, UsageRecord)
```

The same retrieval + ask path powers the grounded Q&A copilot (`AskService`) and
streams answers over SSE with citations.

## 2. Layer and port boundaries

- `HR.Domain` — entities, value objects, errors, pure logic (bias detector,
  injection detector, refusal contract, rubric math). **No SDKs, no IO.**
- `HR.Application` — ports (interfaces) and orchestration/agents/workflows; may
  speak only to its own `Abstractions`.
- `HR.Infrastructure` — adapters (EF Core, providers, parsers, hybrid retriever,
  prompt catalog) and the composition root `HrInfrastructureModule`.
- `HR.API` — auth/middleware/endpoints only.

Branching diagnostics → port changes: adding a capability must not reach into
Domain/Application with an SDK; it lands as a port there and an adapter in
Infrastructure.

## 3. Agent skills (reusable prompt assets)

| Skill file | Apply when |
|---|---|
| `domain-architect.md` | Entity/domain modelling, Clean Architecture, D6 bias-guard rules. |
| `arabic-nlp-specialist.md` | Arabic/English retrieval, normalization (T1). |
| `rag-engineer.md` | Ingestion, chunking, hybrid retrieval, refusal logic. |
| `security-auditor.md` | OWASP Web/LLM Top 10 controls, threat review. |
| `multi-agent-orchestrator.md` | Agent contracts, orchestration, approval gate. |
| `teaching-pack-creator.md` | Slides/labs/assessment deliverables. |

A task that matches a skill must load it first (the tooling reads it before
editing).

## 4. Quality gates for any change

1. `dotnet build HR.sln -warnaserror` — green (treat-warnings-as-errors).
2. `dotnet format HR.sln --verify-no-changes` — formatting enforced in CI.
3. `dotnet test HR.sln` — suite (currently 50 tests) green.
4. Behaviour-change ⇒ rerun `dotnet run --project HR.Evaluation` and update
   `docs/EVALUATION.md` with real numbers.
5. Deferral/cut ⇒ update the gap table in `docs/SYSTEM-DESIGN.md` (Part B).
6. Commit atomically (Conventional Commits, `why:` body).

CI runs restore `--locked-mode`; after touching any `.csproj` the regenerated
`packages.lock.json` must be committed.

## 5. PR workflow (simulated)

There is no remote. Feature work lives on `pr/<name>` branches, is reviewed in
`docs/PR-LOG.md`, and merged with `git merge --no-ff` (preserving the merge
commit). Issues are tracked as numbered files under `docs/issues/`.

## 6. Honesty rules

- Never invent evaluation numbers; the harness produces them
  (`docs/evaluation/local-baseline.json`).
- Never commit a secret; the CI gitleaks scan checks full history.
- Synthetic data only. The corpus is explicitly invented and says so.
- Never comment out dead code; delete it.