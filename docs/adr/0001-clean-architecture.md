# ADR 0001 — Architecture pattern: Clean Architecture (Ports & Adapters)

- **Status:** Accepted
- **Date:** 2026-09-12
- **Decision needed:** Which architecture discipline for the codebase?
- **Decision:** Clean Architecture with a strict 4-project dependency rule:
  `HR.Domain → HR.Application → HR.Infrastructure → HR.API`. Ports (interfaces the
  application needs: LLM, embeddings, vector store, keyword store, persistence,
  usage recording, tracing) are declared in `HR.Application`. Adapters live in
  `HR.Infrastructure`.
- **Alternatives considered and rejected:**
  - *Layered (controller→service→repository):* service layer silently accumulates
    framework types; the LLM/vector-store swap test becomes a refactor, not a config change.
  - *Vertical slices:* excellent for read-heavy CRUD apps; the agent pipeline is a
    pipeline through shared domain entities and ports, and slice boundaries would be
    duplicated for little gain here.
  - *Hexagonal/Onion:* functionally equivalent to Clean for our purposes; Clean's
    explicit, enforced project layout communicates the rule to every contributor (and to
    the AI tooling) better than discipline alone.
- **Consequences:** The acceptance test is greppable: no `HttpClient`, `Npgsql`,
  `OpenAI`, `Ollama`, or web-framework types may appear in `HR.Domain`/`HR.Application`.