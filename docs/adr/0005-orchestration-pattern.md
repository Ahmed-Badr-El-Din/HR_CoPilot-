# ADR 0005 — Orchestration pattern: explicit staged pipeline with an approval gate

- **Status:** Accepted
- **Date:** 2026-09-18
- **Decision needed:** How should the D6 screening flow coordinate its multiple
  agents, tools and state transitions?
- **Decision:** A single explicit *staged orchestrator* (`ScreeningOrchestrator`,
  `HR.Application/Workflows`) drives the workflow as a documented state machine:

  ```
  queued → running
    → [EvidenceExtractorAgent]  (per candidate, redacted evidence)
    → [RubricScorerAgent]       (scores on redacted evidence only)
    → [ShortlistDrafterAgent]   (draft + interview probes)
    → awaiting-approval
    → (approve | reject | edit-and-approve) → completed | failed
  ```

  Agents are plain instances with typed public methods
  (`RunAsync(EvidenceExtractionRequest, CandidateContext, AgentServices, RunId, Ct)`),
  not autonomous loops; the orchestrator owns ordering, timeouts
  (`PerStepTimeout`), correlation, run-event emission and the approval gate
  (`ApprovalRole = HiringManager`). The only writes (draft publish, shortlist
  write) go through a fixed `IToolRegistry` and are rejected unless backed by an
  approved `ApprovalRequest`.
- **Alternatives considered and rejected:**
  - *AutoGen/CrewAI-style autonomous agent frameworks:* heavy external
    dependencies, non-deterministic loops, and the framework owns state — we need
    a persisted, auditable, testable pipeline where every step is a typed call we
    control. Also violates the port boundary (SDKs only in Infrastructure).
  - *Event-sourced workflow engine (MassTransit/Workflow Core/Temporal):* correct
    for distributed, long-running flows with heterogeneous worker fleets; this
    deployment is a single process and the flow is bounded (≤5 iterations), so the
    state-machine orchestrator is cheaper and easier to reason about. The SSE
    `IEventEmitter` gives the same progress feed without a broker. If the broker
    gap table row is later closed, only the orchestrator's mid-point persistence
    changes, not the agents.
  - *Publish/subscribe agents reacting to domain events:* decoupled but makes
    sequencing implicit; the approval gate *requires* explicit control flow
    (a publish must not race an approval).
- **Consequences:** the D6 flow is fully deterministic in ordering, provenance is
  traceable per run, and the harness/unit tests can drive the whole pipeline
  without a framework. The cost is that adding a fifth agent means editing the
  orchestrator's stage list — an acceptable trade for correctness and audit. This
  ADR retroactively documents the pattern introduced with the Phase-1 skill
  hardening and is the fifth ADR, closing the "≥4 ADRs" briefing item.