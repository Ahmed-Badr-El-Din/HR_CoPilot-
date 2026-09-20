# Issue 015 — Packaging, release, submission readiness

- **Type:** build · **Milestone:** M6 · **Status:** open
- **Scope:** `docker compose up` (Postgres+pgvector, Redis, API), seed/ingest command,
  .env.example completeness, CI green + branch protection + secret scan over full history,
  ≥30 commits / ≥6 days / ≥8 PRs, release tags, compatibility with local Ollama provider,
  video-record placeholders or links, submission reply draft.
- **Why:** Definition of Done.
- **Acceptance:** fresh `docker compose up` in an empty clone boots the whole system; user
  can run the 5-minute demo path against the containerised stack.