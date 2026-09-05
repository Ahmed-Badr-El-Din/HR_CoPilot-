# Contributing

Thanks for contributing to HR Copilot. This file encodes the workflow this repo runs on —
the same workflow described in `docs/AGENTIC-WORKFLOW.md`.

## Commit convention

[Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):

```
<type>(<scope>): <imperative summary>

Why: <why the change exists — what it enables or fixes>
```

- `type`: `feat`, `fix`, `refactor`, `chore`, `docs`, `test`, `build`, `perf`, `revert`.
- Atomic commits only. No WIP, no "fix2", no 4,000-line dumps, no commented-out code.
- The **why** is mandatory. The repository is read as a story, not a snapshot.

## Branching & pull requests

- `main` is protected. **Nobody pushes to `main` directly — not even solo.**
- Work happens on `feature/<issue>-<slug>` branches and lands via PR.
- Every PR references its issue (`Closes #12`) and carries a description: **what / why /
  how tested**, plus a self-review with inline comments.
- CI must be green before merge (build, format check, tests, dependency & secret scan).
- Release increments are tagged (`v0.1.0`, …) at merges that clear a milestone.

## Local quality gates (mirror of CI)

```
make build      # dotnet build -warnaserror
make lint       # dotnet format --verify-no-changes
make test       # dotnet test
make scan       # secret + dependency scan
make ci         # build + lint + test + scan
```

## Definition of Done

Something is done only when: tests pass, `make ci` is green on the branch, the change is
self-reviewed, and the PR description explains *why* and *how it was tested*.