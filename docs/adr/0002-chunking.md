# ADR 0002 — Chunking: structure-aware, heading-first segmentation

- **Status:** Accepted
- **Date:** 2026-09-12
- **Decision needed:** How to split documents into retrievable, citable chunks.
- **Observation:** The corpus is HR material — job descriptions, competency frameworks,
  interview rubrics, policies — written in headed sections, clauses and bullet lists, in
  both English and Arabic. Semantic meaning lives inside a section (a competency definition,
  a rubric criterion), rarely across section boundaries.
- **Decision:** Three-level strategy, in order:
  1. **Hard boundaries first:** heading lines (`#`/`##` in markdown, persistent outline
     levels in DOCX, bold/underlined heading heuristics in PDF) and clause delimiters
     (`Article`, `المادة`, numbered clauses) always start a new chunk.
  2. **Fill to target** with paragraph/sentence granularity up to `maxTokens` (default 350
     tokens ≈ 900 chars average, configurable), carrying the section/page reference.
  3. **Overlap 10%** on oversized blocks so a sentence spans at most two chunks.
  References: `section` + `pageReference` ("p.3", "clause 4.2", "المادة 5") attach to every
  chunk for citations.
- **Alternatives considered and rejected:**
  - *Fixed-size token slicing:* shreds competency definitions and rubric criteria; cheap but
    wrong for the document structure.
  - *Sentence-only splitting:* ignores structure, orphans clause boundaries.
  - *Semantic/Late-chunking via LLM:* expensive per ingest; not needed at corpus scale.
- **Consequences:** Chunks map 1:1 to a section/clause; citations are exact; retrieval
  quality is directly sensitive to heading extraction quality (we measure it).