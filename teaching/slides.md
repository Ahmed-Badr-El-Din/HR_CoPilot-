# Slides — RAG beyond the demo (90 minutes)

Course title: **Agentic RAG that refuses, redacts and requires approval.**
Case study: the D6T1 HR Copilot in this repository.

1. **Claim** — A demo RAG ≠ a product RAG. Three gaps kill production RAG:
   hallucination, injection, and uncontrolled agency. We close all three.
2. **The product** (5 min) — HR talent screening: role + candidate pool →
   evidence → rubric scores → shortlist → *human approval*. Inputs arrive as
   resumes; outputs ship as audited decisions.
3. **The stack** (5 min) — .NET 10, ASP.NET Core minimal API, EF Core + SQLite,
   JWT Identity, hybrid retrieval, provider-agnostic LLM port.
4. **Architecture law** (5 min) — `Domain → Application → Infrastructure → API`.
   Grep-acceptance: no SDK types in Domain/Application. Why: the LLM and vector
   store are swappable by configuration, not by refactor.
5. **Grounding ≠ retrieval** (7 min) — retrieve-then-read looks grounded, but a
   fluent answer can still be wrong. Grounding is an *operational* contract:
   cite a chunk, quote it, or refuse.
6. **The refusal contract** (5 min) — `HR.Domain/Common/Refusals.cs`. A provider
   sentinel is promoted to a first-class refusal. Metrics treat refusal as a
   correct outcome, not a failure.
7. **Measuring it** (7 min) — the FR-3 harness: golden set, adversarial set,
   AR/EN-separated metrics, real numbers in `docs/EVALUATION.md` (hit@5 81%,
   answer rate 86.5%, refusal accuracy 100%).
8. **Bias is a leak, not a feeling** (7 min) — protected attributes are removed
   deterministically *before* scoring (`ProtectedAttributeDetector`), every
   removal is audited, and the output shortlist is re-validated. Deterministic
   code does the math; the LLM never computes totals.
9. **Prompt injection, direct** (7 min) — a bilingual detector
   (`PromptInjectionDetector`) refuses override instructions *before retrieval*.
   Demo: "ignore all previous instructions…" → 429-style refusal event.
10. **Prompt injection, indirect** (7 min) — the harder case: instructions *inside
    documents*. Neutralise, don't just warn. The adversarial corpus proves 0%
    leak end-to-end.
11. **The hidden fourth bug pattern** (5 min) — "it works in the demo" can hide
    silently broken plumbing: our harness found RRF confidence unreachable
    (every question refused) and the first streamed token dropped. *Test the
    property, not the happy path.*
12. **Excessive agency** (7 min) — agents have a *fixed, enumerated* tool list; a
    write requires an approved `ApprovalRequest`. Gating is structural, not a
    style guideline.
13. **Observability for LLMs** (5 min) — correlation ID from request to outbound
    call (`X-Correlation-Id`), per-run token/cost records, health/readiness.
14. **Fallback is a security property** (5 min) — provider outage degrades to
    *extractive* answers, not invention. Fail-safe = still grounded.
15. **Rate limiting and auth** (5 min) — per-client token bucket, three
    server-enforced roles, JWT with issuer/audience/lifetime validation.
16. **Deferrals are design** (5 min) — the honest gap table
    (`docs/SYSTEM-DESIGN.md` Part B): gateway, pgvector, Redis, broker —
    each with an interim mitigation and a cost to close.
17. **Shipping discipline** (5 min) — `-warnaserror`, format gate, locked restore,
    committed lock files, gitleaks history scan, 50 tests, reproducible eval.
18. **The takeaway** (3 min) — build the *guard rails* first: refusal, redaction,
    injection defence and approval. Then measure them. A demo that refuses is
    worth more than a demo that answers.

Discussion prompts for the final 15 minutes:
- If you moved the vector store to pgvector, what must stay in the Domain?
- How would you phrase the refusal sentinel for a *multilingual* hosted model?
- Which gap-table row would you close first, and why?