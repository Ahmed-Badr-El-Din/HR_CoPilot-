# ADR 0004 — Twist T1: one bilingual pipeline, language-aware normalisation (not two systems)

- **Status:** Accepted
- **Date:** 2026-09-12
- **Decision needed:** How to support Arabic + English ingestion, retrieval and cross-lingual
  queries without building two products.
- **Decision:** A **single retrieval pipeline with language-aware normalisation**:
  - One chunk pipeline; every chunk carries `language` (AR/EN/auto-detected).
  - **Dense:** one multilingual embedding model does cross-lingual matching natively
    (nomic-embed-text is multilingual). No per-language model.
  - **Keyword:** one BM25 index whose tokeniser is **script-aware** — Latin tokens get
    English light stemming, Arabic tokens get diacritic/tatweel removal, alef/hamza/yeh
    normalisation and light suffix stripping *before* indexing and at query time, so
    Arabic keyword search works (`مطلوب` matches `مطلوبة`; `لا` and `ال` don't block).
  - **Cross-lingual queries (T1):** query terms are transcribed/translated against a
    bilingual HR career glossary (deterministic, seeded in code) and expanded; when an LLM
    is available, an optional one-shot translation prompt enriches the expansion set.
    Search is then language-agnostic; results are filtered/boosted by the requested
    language when the UI pins one.
  - **RTL (T1):** every UI surface derives `dir="rtl"` from a language selector; mixed
    bidi strings rendered with explicit directional markers.
- **Alternatives considered and rejected:**
  - *Two completely separate AR/EN databases and pipelines:* doubles maintenance, breaks
    cross-lingual retrieval by construction, and still needs a translation layer — rejected
    as the worst form of "simplicity".
  - *English-only storage with Arabic translated at ingest:* translation cost per doc,
    drift between the translated and original text (breaks exact-chunk citations), and
    translates away the ground truth. Rejected.
  - *Late fusion of separate AR-only and EN-only retrievers:* plausible, but the measurable
    gain at our scale is below the cost of running two retrievers; a single multilingual
    dense model + script-aware BM25 ships it now. Revisited if Arabic benchmark numbers are
    weak (tracked in `docs/EVALUATION.md`).
- **Consequences:** Arabic metrics must be reported **separately** from English — the eval
  harness splits the golden set by language and by cross-lingual direction.