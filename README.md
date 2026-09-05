# HR Copilot — Domain Copilot, Variant **D6T1**

An agentic RAG platform for **HR talent screening**. A hiring manager supplies a
role and a candidate pool; specialised agents extract **competency evidence**,
score it against a **rubric**, and draft a **shortlist with interview probes** —
but a human hiring manager approves anything consequential. Protected attributes
never reach the scorer, citation snippets are redacted, prompt injection is
refused end-to-end, and an audit trail proves all of it.

Built for the ITI Technical Instructor technical assessment (Domain Copilot —
"Agentic RAG Platform"). Stack: **.NET 10 / ASP.NET Core / EF Core / SQLite /
JWT Identity / deterministic offline `local` provider + OpenAI-compatible adapter**.

> **Status:** v1.0.0 — inspected, built, formatted, tested (50 tests), evaluated and
> packaged. See `docs/SUBMISSION.md` for the submission guide.

## Variant derivation

- **D6 — HR, talent screening** — `last two digits of National ID mod 7 = 6`.
- **T1 — Bilingual (AR + EN)** — `sum of all digits mod 8 = 1`.

Workflow: *role + candidate pool → extract competency evidence → score against
rubric → shortlist + interview probes*, with a **hiring-manager approval gate**
and **bias** as the guarded risk.

## Quick start

Requires the .NET 10 SDK. No API key needed: the default provider is a
deterministic offline model and the full path (chat, screening, eval) works.

```bash
dotnet restore --locked-mode HR.sln
dotnet build HR.sln -warnaserror
dotnet test HR.sln

dotnet run --project HR.API                    # cwd = HR.API → hr.db created there
#   → http://localhost:5000/swagger  (docs + interactive UI)
#   Demo accounts (on first boot): admin / manager / auditor @ hr.local, pass ChangeMe1!
```

Run the FR-3 evaluation harness and reproduce the numbers in `docs/EVALUATION.md`:

```bash
dotnet run --project HR.Evaluation --configuration Release -- --output docs/evaluation/local-baseline.json
```

Run the same stack containerised:

```bash
docker compose up --build          # seeds the synthetic corpus on startup
```

## Feature summary

| Capability | Where |
|---|---|
| Multi-format ingestion (txt / md / json / pdf / **docx**) | `HR.Application/Documents` + parsers in `HR.Infrastructure/Processing` |
| Bilingual hybrid retrieval (dense + keyword, RRF, T1 expansion) | `HR.Infrastructure/Retrieval/HybridRetriever.cs` |
| Grounded Q&A with citations + explicit refusal | `HR.Application/Workflows/AskService.cs`, `HR.Domain/Common/Refusals.cs` |
| Agentic screening pipeline + approval gate | `HR.Application/Workflows/ScreeningOrchestrator.cs` |
| Bias guard (redact → score → validate → audit) | `HR.Domain/Bias`, `HR.Infrastructure/Seed/AuditBiasWriter.cs` |
| Prompt-injection guard, direct + indirect | `HR.Domain/Security/PromptInjectionDetector.cs` |
| Streaming SSE answers with cancellation (R-6) | `AskService` + `HR.API/Endpoints` |
| JWT auth with 3 enforced roles, rate limiting | `HR.API/Program.cs`, `HR.API/Auth` |
| EF Core migrations, provider retry/backoff, correlation tracing | `HR.Infrastructure` |
| FR-3 evaluation harness (golden + adversarial sets) | `HR.Evaluation` |

## Documentation index

| Doc | Purpose |
|-----|---------|
| `docs/BRD.md` | Business requirements, personas, traceability matrix |
| `docs/SYSTEM-DESIGN.md` | Target architecture (Part A) + MVP + honest gap table (Part B) |
| `docs/ARCHITECTURE.md` | C4 L1–3, sequence & data-flow diagrams, layer rule, ADRs |
| `docs/AGENTIC-WORKFLOW.md` | The D6 product pipeline and the repo's agent workflow |
| `docs/SECURITY.md` | Threat-by-threat controls (OWASP Web + LLM top 10) |
| `docs/EVALUATION.md` | Real baseline numbers, methodology, failure analysis |
| `docs/PR-LOG.md` | Pull-request log (simulated locally; no remote) |
| `docs/SUBMISSION.md` | What ships, how to run it, and the honest caveats |
| `skills/` | Versioned prompt assets used by the agent skills |

## Honest caveats

- `local` is a deterministic extractive model, not a neural LLM — deliberate so
  the demo/eval needs no API key. Swap via `Llm__FallbackOrder=openai,local` and
  `Llm__ApiKey` when a hosted tier is available.
- The MVP runs a single SQLite file; the path to Postgres/pgvector, Redis, a
  gateway and a broker is documented row-by-row in `docs/SYSTEM-DESIGN.md` Part B.
- All data is synthetic. No real personal data exists in the corpus.