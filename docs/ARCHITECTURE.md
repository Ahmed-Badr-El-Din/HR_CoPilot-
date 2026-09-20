# Architecture

**Variant:** D6T1 · **Status:** MVP implemented, evaluated and packaged (v1.0.0) · updated 2026-09-18

## Layer dependency rule (enforced by `Directory.Build.props` warnaserror + review)

```
HR.Domain ──────────────> (nothing; entities, value objects, errors, pure logic)
HR.Application ─────────> HR.Domain        (ports as interfaces, agents, workflows)
HR.Infrastructure ──────> HR.Application   (all SDK adapters + composition root)
HR.API ────────────────> HR.Infrastructure (+Application/Domain transitively)
```

Acceptance grep: no `HttpClient`/web-framework/DB/SDK types in `HR.Domain` or
`HR.Application`.

## C4 L1 — System context

```mermaid
flowchart LR
    HM[("Hiring Manager")]
    CAND[("Candidates / HR admin")]
    PEOPLE[("Auditor")]
    SYS["HR Copilot (D6T1)\nagentic bilingual screening\n+ grounded Q&A copilot"]
    LLM(["LLM provider\nOpenAI-compatible or\nlocal deterministic"])
    HM -->|"role + candidate pool\napprove/reject shortlist"| SYS
    CAND -->|"resumes (txt/md/json/pdf/docx)"| SYS
    PEOPLE -->|"audit trail, usage"| SYS
    SYS -->|"embeddings + completions\n(X-Correlation-Id)"| LLM
```

## C4 L2 — Containers

```mermaid
flowchart LR
    CLI["Browser / API clients\n(EventSource for SSE)"]
    API["HR.API\nminimal API, auth,\nrate limit, SSE"]
    INFRA["HR.Infrastructure\nEF Core + SQLite,\nparsers, hybrid retriever,\nproviders"]
    APP["HR.Application\nAskService, ScreeningOrchestrator,\nagents, ports"]
    DOM["HR.Domain\nentities, bias guard,\nrefusal, injection guard"]
    EVAL["HR.Evaluation\nFR-3 harness"]
    LLM(["local | openai"])
    CLI -->|HTTPS/JWT| API
    API --> INFRA
    INFRA --> APP
    APP --> DOM
    EVAL --> INFRA
    INFRA -->|completions/embeddings| LLM
```

## C4 L3 — Primary components (screening flow)

```mermaid
flowchart TD
    ORCH["ScreeningOrchestrator"]
    EXT["EvidenceExtractorAgent\n(redacts protected attributes,\nneutralizes injected instructions)"]
    BR["BiasWriter → BiasAudits"]
    SCORER["RubricScorerAgent\n(deterministic weights, redacted input only)"]
    DRAFT["ShortlistDrafterAgent\n(draft + interview probes)"]
    GATE["Approval gate\n(role = HiringManager)"]
    PUB["PublishShortlistTool\n(gated write)"]
    AUDIT["AuditLogs + UsageRecord"]
    ORCH --> EXT --> BR
    ORCH --> SCORER --> BR
    ORCH --> DRAFT --> GATE --> PUB
    GATE --> AUDIT
```

## Sequence diagram — grounded Q&A (R-6)

```mermaid
sequenceDiagram
    participant C as Client (SSE)
    participant A as API
    participant AS as AskService
    participant PG as PromptInjectionDetector
    participant R as HybridRetriever
    participant LP as Provider chain
    C->>A: GET /api/sessions/{id}/ask?q=...
    A->>AS: StreamAsync(question, correlationId)
    AS->>PG: Scan(question)
    alt direct injection
        AS-->>C: event: refused
    else safe
        AS->>R: RetrieveAsync(question)
        R-->>AS: chunks (top-8, RRF)
        AS->>PG: Neutralize(chunk texts)
        AS->>LP: StreamAsync(grounded-answer, X-Correlation-Id)
        LP-->>AS: tokens (or refusal sentinel)
        AS-->>C: event: token* / citation* / refused / done
    end
```

## Data-flow diagram — what the LLM provider sees (trust boundaries)

```mermaid
flowchart LR
    IN["ingested resume"] --> RED["ProtectedAttributeDetector.Exclude"]
    RED --> QUOTE["redacted evidence quotes\n(no age/gender/nat/IDs/contacts)"]
    QUOTE --> CTX["scorer context"]
    INJ["retrieved chunks"] --> NEUT["PromptInjectionDetector.Neutralize"]
    NEUT --> CTX
    CTX --> LLM(["LLM provider"])
    LLM --> SCORE["typed scores/evidence records"]
    B["BiasAudits (snapshot of every redaction)"] -. audit .- RED
```

The provider never sees the raw intake: protected attributes are cut before
tokenization, embedded instructions are neutralised before formatting, and the
approved shortlist is re-validated deterministically (`ValidateShortlistTool`)
before it can be published.

## ER diagram (core persistence)

```mermaid
erDiagram
    DOCUMENTS ||--o{ DOCUMENT_CHUNKS : "has"
    DOCUMENTS ||--o{ RUNS : "ingests"
    SESSIONS ||--o{ SESSION_MESSAGES : "contains"
    RUNS ||--o{ RUN_EVENTS : "emits"
    RUNS ||--o{ USAGE_RECORDS : "counts"
    RUNS ||--o{ APPROVAL_REQUESTS : "requires"
    RUNS ||--o{ BIAS_AUDITS : "records"
    DOCUMENTS { guid id }
    DOCUMENT_CHUNKS { guid id; float[] embedding }
    SESSIONS { guid id; string owner_user_id }
    RUNS { guid id; int kind; int status; string correlation_id }
```

## Architectural decision records

- `docs/adr/0001-clean-architecture.md` — strict port-and-adapter layering.
- `docs/adr/0002-chunking.md` — structure-aware chunking.
- `docs/adr/0003-vector-store.md` — vector store behind a port (pgvector deferred).
- `docs/adr/0004-bilingual-retrieval.md` — bilingual query expansion + RRF fusion.
- `docs/adr/0005-orchestration-pattern.md` — explicit staged pipeline + approval gate.