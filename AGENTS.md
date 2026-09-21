# AGENTS.md — rules for AI tooling in this repository

This file governs how AI agents (opencode sessions, sub-agents, and any other tooling) work
in this repo. It is committed, versioned, and referenced by `docs/AGENTIC-WORKFLOW.md`.

## 1. Non-negotiable architecture boundary

Do not add LLM SDKs, vector-store SDKs, HTTP client code, ASP.NET/web-framework types, or
DB-driver types to `HR.Domain` or `HR.Application`. Those layers speak only to **ports**
(interfaces declared in `HR.Application/Abstractions`). All adapters live in `HR.Infrastructure`.

Any new package must belong to `HR.Infrastructure` or `HR.API` — never Application/Domain.

## 2. Layering

```
HR.Domain────────────> nothing (entities, value objects, errors, pure logic)
HR.Application───────> HR.Domain  (ports, services, agents, workflows, policies)
HR.Infrastructure────> HR.Application (adapters + composition root)
HR.API───────────────> HR.Infrastructure (+ Application/Domain transitively)
```

`HrInfrastructureModule.AddHrInfrastructure(IConfiguration)` is the only composition root.
`HR.API/Program.cs` should only register auth/middleware and map endpoints.

## 3. Commands

```bash
dotnet build HR.sln -warnaserror                 # must stay green (target net10.0)
dotnet format HR.sln --verify-no-changes
dotnet test HR.sln
dotnet run --project HR.API                      # API; cwd = HR.API so hr.db is created there
dotnet run --project HR.Evaluation               # FR-3 eval harness: golden + adversarial metrics
docker compose up --build                        # containerised API + corpus seed
```

CI is the source of truth (`.github/workflows/ci.yml`); it runs, in order:

```bash
dotnet restore --locked-mode HR.sln
dotnet build HR.sln --configuration Release --no-restore -warnaserror
dotnet format HR.sln --verify-no-changes --no-restore --verbosity diagnostic
dotnet test HR.sln --configuration Release --no-restore
dotnet list HR.sln package --vulnerable --include-transitive
```

- CI restores `--locked-mode`: after adding/updating a package you **must commit the
  regenerated `packages.lock.json`** for every affected project or CI fails. Do not hand-edit them.
- Single test: `dotnet test HR.sln --filter FullyQualifiedName~<ClassName>`.
- `HR.Tests` holds **50 tests across 15 files** (ingestion, bilingual retrieval, Arabic
  normalization, bias guard, DOCX parsing, prompt injection, provider retry, prompt catalog).
- `scripts/scan-secrets.sh` runs gitleaks via Docker. The `make` targets remain a thin,
  partly-stale wrapper; run the `dotnet` commands directly if a target misbehaves.

## 4. Configuration, providers, secrets

- Real option sections are `Llm`, `Storage`, `Auth`, `Ingestion`, `RateLimit` (see
  `HR.Infrastructure/Providers/Options.cs`). Env-var form uses double underscore:
  `Llm__ApiKey`, `Llm__FallbackOrder`, `Auth__JwtSigningKey`, `Storage__ConnectionString`.
- `.env.example` is aligned with the real `Llm/Storage/Auth/Ingestion/RateLimit` sections. The app
  does not load `.env`; use env vars or `appsettings.json`. The Makefile is stale — prefer the
  `dotnet` commands above.
- Provider fallback names are `openai` and `local` (default `Llm__FallbackOrder=local`).
  The `local` provider is deterministic/offline and implements completion, streaming, tool
  calling and embeddings — the full test/demo path needs no API key.
- Never commit a secret; gitleaks scans full history in CI.

## 5. Repo-specific gotchas

- DB schema is created with **EF Core migrations** (`HR.Infrastructure/Migrations`) applied by
  `MigrateAsync()` at startup. Entity changes need `dotnet ef migrations add <Name>` (design-time
  package is referenced by `HR.API`); deleting the SQLite file rebuilds from scratch.
- Strong IDs (`DocumentId`, `ChunkId`, `RunId`, `ApprovalId`, `SessionId` in
  `HR.Domain/Common/StrongIds.cs`) are Guid record structs. The API registers
  `StrongIdJsonConverterFactory` so they bind/serialize as UUID strings; `UserId` is string-backed.
  New endpoints taking these IDs rely on that converter or bind them as `string` + parse manually.
- Microsoft.OpenApi is pinned to 2.x: model types live under `Microsoft.OpenApi` (there is no
  `.Models` sub-namespace). Other packages are pinned to patched versions — read the inline
  comments in the `.csproj` files before bumping.
- Identity is registered via `AddIdentityCore`; `AddDefaultTokenProviders` is unavailable on it.
- Bilingual logic is in `HR.Infrastructure/Nlp` (`TextNormalizer`, `BilingualLexicon`).
  Prompts are versioned JSON assets in `HR.Infrastructure/Prompts/assets`, embedded as resources.
- There is no git remote: PR workflow is simulated with `git merge --no-ff` feature branches and
  recorded in `docs/PR-LOG.md` and `docs/issues/`.

## 6. Conventions

- Commits: Conventional Commits, atomic. Body must explain **why** (`why: ...`), matching history.
- Never comment out dead code; delete it. Synthetic data only; never real personal data.
- Warnings are errors (`TreatWarningsAsErrors`, `AnalysisLevel=latest`, centralized in
  `Directory.Build.props`). Fix warnings; do not suppress.
- Evaluation numbers are real numbers produced by the harness — never invented.

## 7. Definition of done for any change touching the pipeline

1. `dotnet build HR.sln -warnaserror` green
2. relevant tests updated/passing
3. evaluation numbers updated in `docs/EVALUATION.md` if behaviour changed
4. `docs/SYSTEM-DESIGN.md` gap table updated if a deferral or cut happened
5. commit message explains *why*

## Reusable Skills
This project uses versioned prompt assets located in the `skills/` directory:
- `domain-architect.md` - C# Clean Architecture and D6 Bias Guard rules.
- `arabic-nlp-specialist.md` - T1 Bilingual AR/EN retrieval and normalization.
- `rag-engineer.md` - Ingestion, chunking, hybrid retrieval, and refusal logic.
- `security-auditor.md` - OWASP Web and LLM Top 10 controls.
- `multi-agent-orchestrator.md` - Agent contracts, orchestration, approval gate.
- `teaching-pack-creator.md` - Slides, labs, and assessment materials.

When working on a specific task, read the relevant skill file first to ensure you follow the strict constraints.
