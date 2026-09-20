# Submission

Project: **ITI D6T1 — HR Copilot** (post-graduate capstone)
Commit: `HEAD` of `main` at v1.0.0 · Bundled at `v1.0.0`.

This document is the acceptance hand-off: how to run, what was verified,
what was intentionally deferred, and the claim-by-claim evidence trail.

## 1. What this is

A bilingual (Arabic/English) agentic HR talent-screening copilot:

- **Ingestion** — DOCX/TXT/PDF → normalised, structure-chunked, page-referenced,
  bilingual; 36-document corpus, 181 pages, synthetic only.
- **Retrieval** — hybrid keyword + dense embedding over a hashed-embedding space,
  RRF fusion with a *reachable* confidence bound used for refuse-vs-answer.
- **Agents** — evidence extraction → rubric scoring → shortlist draft; writes are
  gated behind human `ApprovalRequest`s; bias attributes redacted **before** the
  model sees anything and deterministically re-validated after.
- **Security** — bilingual direct- and indirect prompt-injection defence
  (refuse + neutralise), provider-refusal sentinel promotion, rate limiting,
  correlation tracing, JWT auth with three server-enforced roles, gitleaks-scanned
  history.
- **Resilience** — provider fallback chain; on outage the system degrades to
  **extractive** answers (never invention); retries with exponential backoff.
- **Evaluation** — FR-3 harness with golden (35 answerable), unanswerable (6) and
  adversarial (7) sets; metrics in `docs/EVALUATION.md`; raw data in
  `docs/evaluation/local-baseline.json`.

## 2. Run it

```bash
# 1. Developer machine (no LLM key required — deterministic `local` provider)
dotnet build HR.sln -warnaserror
dotnet run --project HR.API            # http://localhost:8000/swagger
# admin@hr.local / ChangeMe1!  |  manager@hr.local  |  auditor@hr.local

# 2. Tests + evaluation
dotnet test HR.sln                     # 50/50 green
dotnet run --project HR.Evaluation --configuration Release -- --output /tmp/eval.json

# 3. Containerised, fully seeded (migrations + demo accounts + 36-doc corpus)
docker compose up --build
curl http://localhost:8080/healthz     # {"status":"ok",...}
```

Environment: `.env.example` maps 1:1 to the real `Llm/Storage/Auth/Ingestion/RateLimit`
option sections (double-underscore form). Secrets are **never** committed; gitleaks
runs over full history (see §4).

## 3. Verified-by-command evidence (run 2026-09-18)

| Gate | Command | Result |
|---|---|---|
| Build (warnings=errors) | `dotnet build HR.sln -warnaserror` | 0 warnings, 0 errors |
| Format | `dotnet format HR.sln --verify-no-changes --no-restore` | passes (exit 0) |
| Tests | `dotnet test HR.sln` | **50 passed / 0 failed** |
| Vulnerable packages | `dotnet list HR.sln package --vulnerable --include-transitive` | 0 vulnerable |
| Secret scan (tree) | `scripts/scan-secrets.sh` | no leaks found |
| Secret scan (history) | `scripts/scan-secrets.sh --full-history` | 53 commits scanned, no leaks |
| Container build | `docker compose build` | image `hr-copilot:latest` built |
| Container boot+seed | `docker compose up -d` | healthy in ~30 s; log: `Seeding finished: 36 ingested, 0 failed` |
| Container smoke | auth → create session → Arabic ask (SSE) | starter + Arabic token deltas + citation events |
| Evaluation (± reproducibility) | `dotnet run --project HR.Evaluation` | overall hit@1 **62.2%**, hit@5 **81.1%**, MRR 0.864, answer 86.5%, refusal 100%, grounded 78.2%, citation 100%; injection refusal 100%, adversarial leak 0%, avg latency ≤60 ms |

Evaluation numbers are produced by the harness on commit; they are real and
reproducible — the `local` provider is deterministic.

## 4. Archival record

- Architecture, decisions and honest deferrals: `docs/ARCHITECTURE.md`,
  `docs/SYSTEM-DESIGN.md` (Part B gap table), `docs/adr/`.
- Security story end-to-end: `docs/SECURITY.md`.
- Requirements traceability BR-01…BR-15: `docs/BRD.md` (BR-10 partial, BR-14/15
  implemented — container + teaching pack).
- Agentic workflow + evaluation methodology: `docs/AGENTIC-WORKFLOW.md`,
  `docs/EVALUATION.md`, `docs/evaluation/local-baseline.json`.
- Teaching pack (slides, lab with answer key, assessment map, common mistakes):
  `teaching/`.

## 5. Deferrals (accepted, costed, mitigated — not hidden)

See `docs/SYSTEM-DESIGN.md` Part B for the full 13-row gap table. In short:
Postgres/pgvector, Redis, API gateway, message broker, semantic chunking, hosted
LLM on CI, vector-gap analysis, cache eviction, fine-tuned embeddings, low-code
studio, obfuscation parameter. Each has an interim mitigation and a cost-to-close;
none leaves the shipped system unsafe — refusals, redaction and approval gating
are structural and do not depend on any deferred component.

## 6. Risks left on the table

1. **Arabic answer rate (75% vs EN 95.2%)** — the deterministic 0.50 grounding
   threshold trades recall for precision and safety. Closing it needs a real
   hosted Arabic-capable model (deferred under "hosted LLM on CI").
2. **Document/source mapping** — `Source` is a file-name string; cross-referencing
   against a managed document store is a deferral.
3. **Deterministic embeddings** — a product deployment should swap `HashingEmbedder`
   for a trained embedding + `pgvector` (documented).
4. **Rate limiting is per-process** — correct per node, not across nodes (gateway
   row in the gap table).

## 7. Demo account credentials (synthetic)

| Role | Email | Password |
|---|---|---|
| Admin | `admin@hr.local` | `ChangeMe1!` |
| Manager | `manager@hr.local` | `ChangeMe1!` |
| Auditor | `auditor@hr.local` | `ChangeMe1!` |

Change these via `Auth__Default*` env vars or `appsettings.json` before any
non-training deployment.