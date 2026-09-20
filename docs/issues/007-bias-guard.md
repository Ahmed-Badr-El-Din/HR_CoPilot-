# Issue 007 — Bias guard + audit trail (D6 risk)

- **Type:** feature · **Milestone:** M2 · **Status:** open
- **Scope:** Scoring input = sanitised competency evidence only. Every candidate transcript
  passes the protected-attribute detector (name, gender, age, nationality, religion, marital
  status, contact, ID); the rubric scorer **cannot** receive raw text. Audit entries per
  candidate: sanitizer hash, patterns matched, evidence chunks cited, rubric version, prompt
  versions; post-run verifier asserts no scoring step touched protected attributes.
- **Why:** The D6 mandated risk; the brief demands an audit trail *proving* exclusion.
- **Acceptance:** a crafted transcript with gender/age/nationality scores with zero leakage;
  audit query returns sanitizer proof; test asserts the scoring DTO has no protected fields.