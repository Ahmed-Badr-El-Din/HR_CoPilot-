# Pull request log

This repo has **no GitHub remote at build time**, so PR mechanics are exercised with real
`git merge --no-ff` feature-branch merges — each entry below is the PR description that a
real GitHub PR would carry (what / why / how tested), plus the `Closes #issue` links.

> **Honest process note:** PRs and Issues are stored as files (`docs/issues/`, this log) and
> workflow is *simulated*, not executed on GitHub. Branch protection, CI on PRs and the
> public board are documented as **deferred** in `SYSTEM-DESIGN.md` Part B — the equivalent
> checks were run locally via `make ci` on every merge.

## PR index

| PR | Branch | Description | Closes | Merge |
|----|--------|-------------|--------|-------|
| 1  | pr/1-scaffold | Replace scaffold, repo hygiene | #1 | e79184c |
| 2  | pr/2-domain-skeleton | Domain model + four-layer solution skeleton | #1 | d05246c |
| 3  | pr/3-ports-abstraction | Application ports and abstractions | #5 | d74ced9 |
| 4  | pr/4-ingestion | Ingestion pipeline (chunker, parser, store) | #2 | a303561 |
| 5  | pr/5-bilingual-retrieval | Bilingual retrieval (dense+keyword, RRF) | #4 | 6bccec2 |
| 6  | pr/6-provider-chain | Provider abstraction, fallback chain, versioned prompts | #5 | 3650c60 |
| 7  | pr/7-agents-orchestration | Agents, tools and state-machine orchestration | #6, #7, #8 | a875ae1 |
| 8  | pr/8-persistence | Persistence, vector store and event emitter | #7, #13 | d72853c |
| 9  | pr/9-corpus-seed | Bilingual synthetic corpus and seeding | #3 | 94c82c0 |
| 10 | pr/phase1-skill-ports | Phase 1 hardening: skill port names, domain completion, test suite | #16 | 8441baa |
| 11 | pr/final-audit | Final audit + submission readiness: eval harness, security, docs, container, teaching | #14, #15, #17, #18 | 2b69451 |

## PR #10 — `pr/phase1-skill-ports` (closes #16)

**What:** Phase 1 of the D6T1 agenda across three skills.

- **domain-architect:** renamed retrieval/chunking ports to the skill vocabulary
  (`IRetriever` → `IRetrievalService`, `IChunker` → `IDocumentChunker`), introduced
  `IArabicNormalizer` as a domain port with an `HR.Infrastructure/Nlp` adapter, completed the
  missing domain shapes `JobRole`/`Competency`, `EvidenceChunk`, `AuditLog`, wired the
  bias-guard audit trail (`IBiasWriter.RecordScorerInputAsync`) and documented the mapping in
  `docs/PHASE1-DOMAIN-MAPPING.md`.
- **arabic-nlp-specialist:** repaired Arabic normalization so hamza/alef/tamarbuta actually
  fold to single retrieval tokens, deduplicated the reverse bilingual glossary, and aligned
  the synthetic corpus JSON casing with the parser — three latent defects the new tests
  surfaced.
- **rag-engineer:** first automated test suite in `HR.Tests` (29 tests): idempotent
  production-pipeline ingestion, structure/page-aware chunking, bilingual hybrid retrieval
  with RRF fusion, citations and document filtering, Arabic folding, deterministic bilingual
  corpus, and protected-attribute redaction.

**Why:** `HR.Tests` was empty so CI proved nothing; retrieval ports didn't carry the skill
names; bilingual normalization was dead code; Screening lacked the domain and audit shapes
the D6 bias guard needs.

**How tested:** `dotnet build HR.sln -warnaserror`, `dotnet format HR.sln --verify-no-changes`
and `dotnet test HR.sln` (29 passed) all green; in-memory SQLite fixture keeps the suite
offline and deterministic.

## PR #11 — `pr/final-audit` (closes #14, #15, #17, #18)

**What:** final audit and submission readiness for the D6T1 agenda.

- **fr-3-eval (C1/C2):** a real `HR.Evaluation` harness (`GoldenSet.cs` 35 answerable +
  6 unanswerable + 7 adversarial, `EvaluationRunner.cs` per-language hit@1/hit@5/MRR/answer/
  refusal/groundedness/citation metrics); real numbers written to `docs/EVALUATION.md` and
  `docs/evaluation/local-baseline.json`. The harness forced out **four latent product bugs**:
  unreachable RRF confidence (every question refused), a dropped first streamed token,
  case-sensitive prompt-asset deserialisation (nothing ever loaded), and provider-refusal
  sentinels treated as answers (`HR.Domain/Common/Refusals.cs`).
- **b-series (B1–B7):** DOCX parsing with page/heading structure, corpus growth to 36 docs /
  181 pages, provider retry with exponential backoff, per-process token-bucket rate limiting,
  bilingual direct/indirect prompt-injection defence (`PromptInjectionDetector` + neutralise),
  correlation-id tracing to the outbound LLM call, and evidence citations cut from redacted
  text. New regression tests: `DocxParserTests`, `PromptInjectionGuardTests`,
  `ResilientProviderTests`, `EmbeddedPromptCatalogTests` (total 50).
- **d-series (D1–D9):** `SECURITY.md` (OWASP Web + LLM mappings), `AGENTIC-WORKFLOW.md`,
  rewritten `SYSTEM-DESIGN.md` (Part B gap table), `adr/0005-orchestration-pattern.md`,
  rewritten `ARCHITECTURE.md` + `README.md`, rewritten `BRD.md` (BR-01..BR-15), `teaching/`
  pack (slides, lab + answer key, assessment map, common mistakes), `SUBMISSION.md`,
  `.env.example` aligned to the real options, `AGENTS.md` de-staled.
- **e-series (E1–E4):** multi-stage `Dockerfile` + `docker-compose.yml` (healthy, fully
  seeded in ~30 s; Arabic SSE ask verified in-container), `scripts/scan-secrets.sh`
  (dockerised gitleaks; tree + full 53-commit history both *no leaks found*), zero
  vulnerable packages, format-gate fix (sources now `charset = utf-8` no BOM), v1.0.0 tag.

**Why:** the brief's checklist required verifiable, real evidence for every claim; earlier
untested plumbing was silently wrong, and the docs described a system that differed from the
code.

**How tested:** `dotnet build HR.sln -warnaserror` (0/0), `dotnet format HR.sln
--verify-no-changes`, `dotnet test HR.sln` (50/50), `dotnet list HR.sln package --vulnerable
--include-transitive` (0), `scripts/scan-secrets.sh` (tree + history clean), `docker compose
up --build` (healthy, 36 docs seeded, streamed Arabic answer with citations), and the FR-3
harness (refusal 100%, injection refusal 100%, adversarial leak 0%).