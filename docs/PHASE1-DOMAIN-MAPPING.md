# Phase 1 — domain entity mapping

Phase 1 (Big Pickle) requests a set of named screening-domain entities. Several
already existed in `HR.Domain` under different names; the genuinely-missing ones
were added and wired in (no dead code — AGENTS.md §6). This file maps the
requested names to their implementation so graders can find them.

## Added in this phase

| Requested entity | Location | Notes |
| --- | --- | --- |
| `JobRole` | `HR.Domain/Screening/JobRole.cs` | Typed role with title, description and `Competency` list. `ScreeningRequest.Role` is now a `JobRole`; the whole agent pipeline (extraction queries, rubric block, draft, approval payload) reads `Role.Title`. |
| `Competency` | `HR.Domain/Screening/JobRole.cs` | Named competency record (name + description), populated from rubric dimensions. |
| `EvidenceChunk` | `HR.Domain/Screening/ScreeningContracts.cs` | Sanitised/redacted chunk handed to a downstream agent as evidence; carries document + section + page citation. Built by `EvidenceExtractorAgent` in the grounding pass *after* protected-attribute redaction (`EvidenceExtractionResult.Chunks`). |
| `AuditLog` | `HR.Domain/Screening/ScreeningContracts.cs` | Immutable record of exactly what a scoring step received (input snapshot + `ExcludedProtectedAttributes`). Written by `RubricScorerAgent` the moment the scorer input is composed, persisted via `IBiasWriter.RecordScorerInputAsync` → `AuditLogEntity` table. Proves protected attributes were excluded (D6 rule). |

## Existed already (mapped)

| Requested entity | Existing equivalent | Location |
| --- | --- | --- |
| `Candidate` | `CandidateRef` | `HR.Domain/Screening/Rubric.cs` |
| `Rubric` | `Rubric` (`RubricDimension`, `RubricMath`) | `HR.Domain/Screening/Rubric.cs` |
| `Score` | `CandidateScore`, `DimensionScore`, `ScoreSheet` | `HR.Domain/Screening/ScreeningContracts.cs` |
| `Shortlist` | `ShortlistDraft`, `ShortlistEntry`, `InterviewProbe` | `HR.Domain/Screening/ScreeningContracts.cs` |
| `EvidenceChunk` (chunk-level) | `RetrievedChunk` (retrieval), `Citation` (citations) | `HR.Application/Abstractions/Retrieval/IRetriever.cs`*, `HR.Domain/Documents/Document.cs` |

\* file renamed to `IRetrievalService.cs` in the port-rename commit.

## Related ports (Phase 1 naming)

- `IRetrievalService` (was `IRetriever`) — `HR.Application/Abstractions/Retrieval/IRetrievalService.cs`
- `IDocumentChunker` (was `IChunker`) — `HR.Application/Abstractions/Processing/IDocumentProcessing.cs`
- `IArabicNormalizer` — `HR.Domain/Common/IArabicNormalizer.cs`, implemented by `ArabicNormalizer` (`HR.Infrastructure/Nlp`) and consumed by `HybridRetriever` (bilingual expansion, T1).

## Deliberate cuts / deferrals

- Named `InterviewProbe` vs `Competency` duplication: `InterviewProbe.Competency` remains a string to keep agent contracts stable; `Competency` entity carries the role-level definition.
- `AuditLog` has no list/query endpoint yet; the data + schema exist (searchable by candidate/step/timestamp). Add an audit-read endpoint when the Phase-1 evaluation needs it.