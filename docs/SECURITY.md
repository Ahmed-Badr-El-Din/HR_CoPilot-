# Security

Security posture for the HR Copilot D6T1 submission, mapped to OWASP LLM Top 10
and Web Top 10. Controls are implemented in code and are summarised here with
file pointers; independent verification for the LLM-specific claims lives in the
adversarial evaluation (`docs/EVALUATION.md`).

## Threat model

Assets: candidate PII/traits within ingested resumes, screening scores and
shortlists, usage records, and the LLM orchestration itself. Trust boundaries:

1. **Client → API** — anonymous or JWT-authenticated, rate limited, upload-capped.
2. **API → Model provider** — outbound only, keyed, correlated, capped retries.
3. **Retrieved content** — untrusted data from a data path, never treated as
   instructions.

## OWASP Web Top 10

| Control | Where |
|---|---|
| Authentication (A01) | JWT bearer (`HR.API/Auth/JwtTokenService.cs`), HS256, issuer/audience/lifetime validated, 30s skew (`HR.API/Program.cs`). |
| Authorization (A01/A12) | Server-side role claims; policies `CanManage/CanScreen/CanAudit/CanApprove/CanSeed` never trust the client (`Program.cs`, `HR.API/Roles.cs`). |
| Secrets (A02) | Signing key + LLM key externalised via env (`Llm__ApiKey`, `Auth__JwtSigningKey`); defaults are `CHANGE_ME` placeholders; gitleaks runs in CI. |
| Rate limiting (A04) | Token bucket per client (authenticated name/IP), 120 tokens/min, burst 40, `429 application/problem+json` with correlation id (`Program.cs`, `HR.Infrastructure/Providers/Options.cs`). |
| Access control / IDOR (A01) | All persisted runs, sessions, usage and approvals are scoped to the owner (`OwnerUserId`) server-side. |
| Upload validation (A05) | `Ingestion:MaxFileBytes` (25 MB) enforced via `FormOptions` and re-validated per request. |
| Security headers & CORS (A05/A13) | Default CORS policy; no broad reflective/unsafe reflection of user input in responses; error envelope is `application/problem+json`. |
| Monitoring (A09/A21) | Correlation id on every request (`CorrelationMiddleware`) copied to every outbound LLM call as `X-Correlation-Id`; usage and audit tables persisted. |
| Supply chain (A06) | Centralised package pins (`Directory.Build.props` + `packages.lock.json` committed, `--locked-mode` restore); CI runs `dotnet list package --vulnerable --include-transitive`. |

## OWASP LLM Top 10

| Risk | Control |
|---|---|
| LLM-01 Prompt injection (direct) | `HR.Domain/Security/PromptInjectionDetector.cs`: bilingual (EN/AR) override-instruction patterns; matches are refused *before retrieval* in `AskService`. Adversarial set: **100% refused**. |
| LLM-01 Prompt injection (indirect) | Every retrieved/ingested chunk is passed through `PromptInjectionDetector.Neutralize` before the model reads it (chat context and agent evidence). Adversarial corpus fixtures (`adversarial-injection-*`) must leak nothing: leak rate **0%**. |
| LLM-02 Sensitive data disclosure (PII leaks, e.g. LFI in agent output) | `EvidenceExtractorAgent` runs every chunk through `ProtectedAttributeDetector.Exclude`; scored context, evidence quotes and **citation snippets** are redacted. `ValidateShortlistTool` deterministically rejects any shortlist text that re-introduces a protected attribute. |
| LLM-03/04 Insecure output handling / OWASP-D6 output => untrusted | Agent records are typed contracts, not free text; the approval gate is a real write-authorization boundary (`PublishShortlistTool` gated by `CanApprove`). |
| LLM-05 Hallucination & wrong decisions | Refusal contract `HR.Domain/Common/Refusals.cs`; low-evidence refusal threshold on retrieval confidence; extractive-only offline fallback; groundedness measured in the harness. |
| LLM-06 Sensitive information disclosure in training data | Corpus is synthetically generated (no real personal data); declared in `CorpusGenerator`, `BRD.md`, and the `corpus-note` document. |
| LLM-08 Excessive agency | Agent tool registry is a fixed, enumerated set (`HR.Application/Tools`); only two tools can write and they require the approval gate. |

## Privacy by design (bias)

The screening pipeline is structurally bias-informed:

1. **Redact before score** — protected attributes (age, gender, nationality, marital
   status, religion, IDs, contact details) are removed from everything the scorer
   can see (`HR.Domain/Bias/ProtectedAttributeDetector.cs`).
2. **Audit the redaction** — every removal is snapshotted to `BiasAudits` with a
   timestamped audit trail.
3. **Deterministic validation** — post-scoring shortlists are re-scanned, and a
   leaked attribute rejects the draft.
4. **Deterministic math** — rubric weights are combined in code (`RubricMath`),
   never by the LLM.

## Operations

- Secrets: `Auth__JwtSigningKey`, `Llm__ApiKey` via env-var double underscore; the
  `local` provider needs none and is the CI default so pipelines run offline.
- Dependency scanning: CI runs `dotnet list HR.sln package --vulnerable --include-transitive`; repository-wide gitleaks history scan.
- Rate-limit tuning: `RateLimit:TokensPerMinute`, `RateLimit:Burst`.
- Fail-open/close: provider outages degrade to grounded extractive answers
  (`plain-rag`); they do not produce unfounded content, which is the fail-safe mode.

## Known residual risks (documented deferrals)

- Migrations exist, but production DB is a single SQLite file behind the API; a
  multi-node deployment must move to Postgres (`Storage:ConnectionString` is
  already the seam).
- JWT signing key rotation and short-lived refresh tokens are not implemented;
  token lifetime is 12h.
- The offline `local` provider is extractive, not a neural model; its refusal and
  grounding behaviour is measured (not claimed) in `docs/EVALUATION.md`.
- No TPM/gov zero-day TPM integration; that remains a human task for production.