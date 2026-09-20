# Issue 008 — Approval gate (Human holds the pen)

- **Type:** feature · **Milestone:** M2 · **Status:** open
- **Scope:** Approval request created when shortlist draft is ready; supports
  **approve / reject / edit-and-approve**, every action audited (who, when, diff).
  Write/side-effecting tools (record decision, notify) may only execute **after** approval.
  ApprovalRequired if a gated tool is attempted pre-approval.
- **Why:** Binding principle 2; excessive-agency control.
- **Acceptance:** pre-approval gated tool → domain error 409; post-approval executes and is
  traceable; each decision persists an audit entry.