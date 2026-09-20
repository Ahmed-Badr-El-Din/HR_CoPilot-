# Issue 002 — Ingestion pipeline (FR-1)

- **Type:** feature · **Milestone:** M1 (ingest + retrieval) · **Status:** open
- **Scope:** Separable stages **extract → clean → chunk → embed → index**; ≥2 input formats
  (Markdown/TXT native + DOCX + PDF); per-document metadata (source, section, page/clause,
  version, language) and chunk metadata; idempotent re-ingestion (content-hash); per-document
  status + failure reporting; Arabic normalisation (diacritics, tatweel, alef/hamza/yeh).
- **Why:** R-1/R-FR-1; no RAG without a trustworthy ingest path.
- **Acceptance:** re-ingesting an unchanged file is a no-op; a corrupt file reports `Failed`
  with a message; Arabic source text normalises deterministically.