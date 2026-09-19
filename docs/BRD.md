# Business Requirements Document (BRD)

**Project:** HR Copilot · **Variant:** D6T1 · **Owner:** ITI Technical Instructor assessment
**Status:** v1.0.0 — requirements implemented and traced · updated 2026-09-18

## 1. Context

An organisation holds a large body of specialised HR documents — job descriptions,
competency frameworks, interview rubrics, policies. Hiring managers and recruiters spend
hours extracting evidence, scoring candidates consistently, and drafting shortlists.

HR Copilot ingests that corpus, answers questions with verifiable citations, and runs the
**D6 talent-screening workflow**: *role + candidate pool → extract competency evidence →
score against rubric → shortlist + interview probes* — while a **hiring manager approves**
anything consequential. Twist **T1**: everything works in Arabic *and* English, including
cross-lingual retrieval.

## 2. Personas

- **Hiring Manager** — approves or rejects every shortlist; sees only redacted evidence and
  citations; enforcement target of the approval gate (fails closed).
- **Recruiter** — uploads resumes, runs screening runs, reads draft shortlists; cannot publish.
- **Administrator** — configures providers/storage, seeds the corpus, manages users.
- **Auditor** — read-only; inspects run history, usage/cost and the bias audit trail.

## 3. Objectives & measurable criteria (real numbers from the FR-3 harness)

| Objective | Criterion | Baseline (local provider, 2026-09-18) |
|---|---|---|
| Find the right document | Hit@5 / MRR | 81.1% / 0.864 |
| Answer only when grounded | Answer rate / refusal accuracy | 86.5% / 100% |
| Never fabricate | Groundedness / citation coverage | 78.2% / 100% |
| Resist prompt injection | Direct-injection refusal / leak rate | 100% / 0% |
| Arabic quality separated | Arabic Hit@5 / answer rate | 93.8% / 75.0% |

Full per-language table and methodology: `docs/EVALUATION.md`.

## 4. Requirements (BR-xx) with acceptance criteria

| ID | Requirement | Priority | Acceptance criteria | Status | Evidence |
|----|-------------|----------|---------------------|--------|----------|
| BR-01 | Ingest ≥2 formats, idempotent, per-doc status | Must | re-ingest is a no-op; bad file reports Failed | Implemented (5 formats) | `IngestionPipelineTests`, `DocxParserTests`, `CorpusSeedService` |
| BR-02 | Bilingual corpus ≥30 docs / 150+ pages, synthetic only | Must | count ≥30/150 | Implemented (36 / 181) | `CorpusGeneratorTests` |
| BR-03 | Hybrid retrieval + citations + refusal | Must | known-query test; low-evidence refusal | Implemented | `HybridRetrievalTests`, `docs/EVALUATION.md` |
| BR-04 | Provider abstraction ≥2 + fallback | Must | config-only swap | Implemented (+retry/backoff) | `ResilientProviderTests`, `HrInfrastructureModule` |
| BR-05 | 3 agents + orchestrator, typed contracts, restricted tools | Must | pipeline driven by typed records | Implemented | `ScreeningOrchestrator`, `HR.Application/Agents` |
| BR-06 | Bias: protected attrs never reach scorer + audit proof | Must | leakage test; audit query | Implemented | `ProtectedAttributeDetectorTests`, `AuditBiasWriter` |
| BR-07 | Approval gate approve/reject/edit + audited | Must | pre-approval gated tool blocked | Implemented | `ScreeningOrchestrator`, `ValidateShortlistTool` |
| BR-08 | Auth + ≥2 genuinely different roles, server-side | Must | role e2e | Implemented (3 roles) | `HR.API/Program.cs`, `HR.API/Roles.cs` |
| BR-09 | SSE token streaming + progress + cancellation | Must | incremental tokens; cancel stops work | Implemented | `AskService`, `HR.API/Endpoints` |
| BR-10 | Minimal UI + sessions + RTL | Must | every capability reachable | Partial (deferred) | Swagger/OpenAPI + persistent sessions; RTL SPA deferred → Part B gap table |
| BR-11 | Eval harness ≥25 QA, ≥5 adversarial, ≥3 injection, AR separate | Must | harness prints metrics | Implemented (48 cases) | `HR.Evaluation`, `docs/EVALUATION.md` |
| BR-12 | Correlation ID, usage/cost accounting, trace store, health | Must | usage queryable by run | Implemented | `UsageAsync`, health endpoints, `X-Correlation-Id` |
| BR-13 | Security controls per SECURITY.md | Must | injection cases resist | Implemented | `PromptInjectionGuardTests`, `docs/SECURITY.md` |
| BR-14 | docker compose up + seed | Must | clean-clone boot verified | Implemented | `Dockerfile`, `docker-compose.yml`, `docs/SUBMISSION.md` |
| BR-15 | Teaching pack + docs + video scripts | Must | all docs present | Implemented | `docs/` index, `teaching/` |

## 5. Out of scope

Explicitly cut or deferred, mirrored from `docs/SYSTEM-DESIGN.md` Part B: API gateway +
managed rate limiting, secrets manager, message broker/async workers, autoscaling, Redis
cache, managed vector DB (pgvector target behind a port), full OTel/Grafana stack, external
IdP, DR automation, and a dedicated RTL SPA (BR-10 partial). Each has an interim mitigation
and a cost-to-close in Part B.

## 6. Business rules

- Scoring uses only sanitised evidence (bias guard) — BR-06.
- Write/side-effecting tools never run before approval — BR-07.
- "Not enough information in the corpus" is a correct answer — BR-03.
- Deterministic code computes weighted totals; the LLM never does arithmetic — D6 discipline.
- Injected instructions in documents are never obeyed (they are neutralised) — BR-13.

## 7. Assumptions

- .NET 10 SDK available for build/test/run; Docker available for the container path.
- No API key is required: the `local` provider is the default and the full path works offline.
- Development storage is a single SQLite file created and migrated at startup.
- All corpus data is synthetic; no real personal data is present or permitted.
- Grading can use `dotnet`, `docs/evaluation/local-baseline.json`, or `docker compose up`.

## 8. Risks

| Risk | Mitigation | Evidence |
|---|---|---|
| Bias leakage into scores | Deterministic redaction → deterministic re-validation → audit | `ProtectedAttributeDetectorTests` |
| Arabic retrieval quality | Bilingual expansion + normalisation; measured | Arabic Hit@5 93.8% |
| Prompt injection via documents | Domain detector + neutralisation | 100% refusal / 0% leak |
| Provider outage | Resilient chain + retry/backoff + plain-RAG degradation | `ResilientProviderTests` |
| Single-instance scaling ceiling | Documented deferral with adapter seams | `SYSTEM-DESIGN.md` Part B |

## 9. Traceability matrix

The table in §4 doubles as the traceability matrix: BR-xx → implemented / partial / deferred
→ evidence (code path, test, or documented deferral). Every row resolves to a real file —
no row claims a green tick without an evidence pointer.