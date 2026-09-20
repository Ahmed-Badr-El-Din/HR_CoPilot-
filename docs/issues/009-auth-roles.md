# Issue 009 — Authentication + roles (FR-8)

- **Type:** feature · **Milestone:** M3 (API/UI) · **Status:** open
- **Scope:** JWT auth; roles **Admin / HiringManager / Auditor** with genuinely different
  permissions enforced server-side by policies, not hidden buttons. PBKDF2 password
  hashing; seeded demo users.
- **Why:** FR-8; approval gate needs a human identity to audit against.
- **Acceptance:** API-level tests prove a HiringManager cannot manage users and an Auditor
  can only read.