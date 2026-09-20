# System Design Document (SDD)

**Variant:** D6T1 · **Status:** MVP implemented and evaluated · last updated 2026-09-18

The most important document in the repo. Two separated parts; candour is a feature.

## Part A — Target architecture (unconstrained)

The production target for this variant is a hosted, horizontally-scaled deployment:

- **Gateway** with managed rate limiting, WAF and edge auth in front of the API;
- **Secrets manager** (Vault/K8s) holding `Auth__JwtSigningKey` and `Llm__ApiKey`;
- **Message broker** (RabbitMQ/NATS) decoupling ingestion from embedding workers;
- **Autoscaling** for API + workers; **Redis** for session/cache; **pgvector** for
  the vector index at scale; **OpenTelemetry + Grafana** observability;
- **DR/backup** + a cost model driven by the already-persisted per-run token/usage
  records.

This part is the *target*; nothing here is implemented. See Part B for the honest
gap table.

## Part B — Implemented MVP

### Gap table: target component | implemented? | why deferred | interim mitigation | effort/cost to close

| Target component | Implemented? | Why deferred | Interim mitigation | Effort/cost to close |
|---|---|---|---|---|
| API gateway + managed rate limiting | No | Single-instance MVP | In-process per-client token bucket (`Program.cs` + `RateLimit`) returning 429 problem+json | Medium — edge gateway config |
| Secrets manager | No | No infra to point at | Env-var options (`Llm__ApiKey`, `Auth__JwtSigningKey`), `CHANGE_ME` defaults, gitleaks CI scan | Small — Vault/K8s secrets |
| Message broker / async ingestion workers | No | First-user demo is synchronous | Direct `DocumentIngestionService` call path | Medium — broker + workers |
| Autoscaling | No | Single process | Stateless API; DB is the only shared state | Medium — k8s HPA |
| Redis cache / session store | No | Sessions already persist in SQLite | Persistent `ChatSession` (R-7) | Medium — swap repository |
| Managed vector DB (pgvector) | No | ADR-0003 deferral | SQLite-embedded vectors behind `IVectorStore` port | Medium — adapter swap |
| Observability stack (OTel + Grafana) | Partial | MVP ships structured logs only | Health/readiness endpoint, usage + audit tables, correlation tracing | Medium |
| CI/CD environments (stage/prod promotion) | No | Single CI workflow | One GitHub Actions workflow: locked restore, build, format, test, vuln scan, gitleaks | Medium |
| DR / backup | No | Synthetic data only | Migrations + seed make a rebuild reproducible | Small — snapshot script |
| Cost model at scale | No | Usage already recorded | Per-run `UsageRecord` (tokens/cost) + pricing caps in `LlmOptions` | Small — FinOps dashboard |
| External IdP (Azure AD/Keycloak) | No | MVP role model is in-process | ASP.NET Identity + JWT with 3 server-enforced roles | Medium |
| Dedicated UI (R-7) | Partial | Scope/effort | OpenAPI/Swagger surface + persistent sessions; SSR minimal page absent | Large — SPA with RTL |
| Guardrail vendor for LLM | No | Deterministic guard preferred | `HR.Domain/Security/PromptInjectionDetector` + eval harness prove refusal/leak rates | Medium |

### Significant design decisions

Each entry: decision · alternatives considered and why rejected. Conditioned on
facts in this repo.

1. **Provider abstraction as a resilient fallback chain** — `IModelProvider` +
   `ResilientModelProvider` (retry/backoff, degrade to next provider) with one
   deterministic offline implementation (`local`) and one hosted (`openai`).
   *Rejected:* a single provider (fails when the free tier — the demo default —
   runs out), provider-specific branches in callers (violates the port boundary).
2. **Hybrid retrieval = dense + bilingual keyword fused by RRF** — keyword index
   serves the Arabic side and dense embeddings the semantic side; RRF avoids
   score-scale coupling. *Rejected:* pure dense (weak on AR morphology at MVP
   embedding dims), pure keyword (no paraphrase tolerance), weighted sum (uncali-
   brated fusion, ADR-0004).
3. **In-process token-bucket rate limiting** — cheap and correct for one instance.
   *Rejected:* a gateway in MVP (no infra). Documented in Part A; must move to the
   edge before horizontal scaling.
4. **Deterministic offline model as the default provider** — the whole test/demo/
   eval path works with no API key. *Rejected:* requiring a hosted key (blocks CI
   and offline demos), no fallback (single point of failure).
5. **EF Core migrations (not `EnsureCreated`)** — schema is versioned now.
   *Rejected:* `EnsureCreatedAsync` (cannot evolve schema), a custom SQL-file
   runner dialect layer (replaced by EF, which also generates the two-dialect
   path; stale writing notes that claimed otherwise are corrected).
6. **Bias guard is deterministic and lives in the Domain** — protected attributes
   are matched by `.NET` regex `HR.Domain/Bias/ProtectedAttributeDetector.cs`.
   *Rejected:* LLM-based detection (nondeterministic, costly, itself prompt-
   injectable). The official result is redacted evidence + deterministic
   validation (`ValidateShortlistTool`), with every redaction audited.
7. **Prompt-injection guard is a deterministic domain classifier** — bilingual
   override patterns (`PromptInjectionDetector`); direct attempts are refused
   pre-retrieval and retrieved content is neutralised. *Rejected:* trusting the
   model (evadable), a third-party guardrail vendor (dependency + cost) before
   the MVP proves the threat locally. Measured: 100% refusal / 0% leak.
8. **Strong IDs as `Guid` record structs serialised as UUID strings** — no
   enumerable integers, no collisions, stable wire format via the
   `StrongIdJsonConverterFactory`. *Rejected:* `long` identity (enumeration and
   scrape risk).
9. **SSE streaming via async iterators + client cancellation** — a token stream
   that actually cancels the provider call. *Rejected:* SignalR (heavier than the
   polling/EventSource use case), naive buffering (breaks R-6).
10. **Versioned prompts as embedded JSON assets** — prompts ship inside
    `HR.Infrastructure.dll`, version-disciplined and immutable at deploy.
    *Rejected:* prompts in DB (deploy/DB coupling), loose files (publish +
    tampering).
11. **Refusal is a first-class outcome** — `HR.Domain/Common/Refusals.cs` sentinel;
    provider refusals are promoted to refused runs/events so clients and metrics
    never see a non-answer as an answer. *Rejected:* streaming refusal text as a
    "normal" answer (ambiguity for clients, skews eval).
12. **Corpus is fully synthetic** — invented to honour "no real personal data"
    while still carrying protected attributes so the bias guard, audit trail and
    adversarial fixtures can be demonstrated honestly. *Rejected:* real/obfuscated
    personal data (risk, licensing).

## Current working notes (MVP)

- Stack locked: .NET 10 / ASP.NET Core minimal API / EF Core + SQLite (pgvector
  target behind `IVectorStore`) / JWT Identity / deterministic `local` provider +
  OpenAI-compatible adapter.
- Clean Architecture with strict 4-project layering — ADR-0001; adapters register
  only via `HrInfrastructureModule` (the sole composition root).
- Schema created and versioned by EF migrations (`HR.Infrastructure/Migrations`);
  startup applies `MigrateAsync`.
- D6 pipeline: bilingual hybrid retrieval → evidence extraction (redacted) →
  rubric scoring (deterministic weights) → shortlist drafting → approval gate
  with audit; details in `docs/AGENTIC-WORKFLOW.md`, `docs/ARCHITECTURE.md`.
- FR-3 numbers are real and reproducible: `docs/EVALUATION.md` +
  `docs/evaluation/local-baseline.json`.