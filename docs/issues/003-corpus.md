# Issue 003 — Bilingual synthetic corpus (≥30 docs / 150+ pages)

- **Type:** data · **Milestone:** M1 · **Status:** open
- **Scope:** Synthetic HR corpus — job descriptions, competency frameworks (with rubric
  dimensions), interview rubrics, HR policies, sample CVs/reference notes — split between
  English and Arabic, explicit "protected attribute" examples (gender/age/nationality in the
  text that the bias guard must remove), and two adversarial documents containing **indirect
  prompt-injection** content hidden in prose. Generator script committed; generated artifacts
  committed so eval is reproducible offline. **No real PII anywhere.**
- **Why:** ≥30 documents / 150+ pages floor; FR-2 needs real structure; FR-3 needs
  ground-truthable sources.
- **Acceptance:** corpus count script ≥30 docs and ≥150 pages; mixed AR/EN; injectable text
  clearly marked in the doc header as synthetic.