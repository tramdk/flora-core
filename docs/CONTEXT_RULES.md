# Context Engineering Rules

Context rules help agents decide what to read, when to read it, and when to
stop reading. The goal is to put the right information in the model for the
current task phase and risk lane — not to maximize context.

## Phase 1: Intake — Classify the request

| Document | Tiny | Normal | High-Risk |
| --- | --- | --- | --- |
| `AGENTS.md` | Must | Must | Must |
| `docs/FEATURE_INTAKE.md` | Must | Must | Must |
| `docs/TEST_MATRIX.md` | Should | Must | Must |
| `Specs/openapi.json` (affected endpoints) | Skip | Should | Must |
| `CODING_POLICY.md` | Skip | Should | Must |
| Relevant `docs/decisions/*` | Skip | Skip | Must |

## Phase 2: Planning — Decide the approach

| Document | Tiny | Normal | High-Risk |
| --- | --- | --- | --- |
| Files to edit (read first) | Must | Must | Must |
| Test file in `FloraCore.Tests/` | Should | Must | Must |
| `docs/stories/*` (if story exists) | Skip | Must | Must |
| `docs/templates/story.md` | Skip | Must when creating story | Must |
| `CODING_POLICY.md` | Skip | Must | Must |
| `docs/decisions/*` (relevant) | Skip | Should | Must |
| Adjacent feature slices with same pattern | Skip | Should | Must |

## Phase 3: Implementation — Write code

| Document | Tiny | Normal | High-Risk |
| --- | --- | --- | --- |
| Files being changed | Must | Must | Must |
| Adjacent files with same pattern | Should | Must | Must |
| Relevant DTOs, interfaces, entities | Must if touching | Must | Must |
| `AppDbContext.cs` | Only if DB-related | Must if entity change | Must |
| `DependencyInjection.cs` | Only if new service | Must if new service | Must |
| Relevant Controller | Should | Must | Must |

## Phase 4: Verification — Prove the work

| Document | Tiny | Normal | High-Risk |
| --- | --- | --- | --- |
| `dotnet build` output | Must | Must | Must |
| `dotnet test` (filtered or full) | Should | Must | Must |
| `./scripts/final-check.ps1` | Skip | Must | Must |
| `Specs/openapi.json` | Skip | Must if API changed | Must |
| `docs/TEST_MATRIX.md` | Skip | Should update | Must update |

## Flora-Core Reading Shortcuts

Common patterns to reduce unnecessary file reads:

| Task type | Start by reading |
| --- | --- |
| New Command handler | `Application/Features/{Feature}/Commands/` + test file + entity |
| New Query handler | `Application/Features/{Feature}/Queries/` + test file + DTO folder |
| New Controller endpoint | Controller file + existing Command/Query + `openapi.json` |
| Bug fix | Test file first → reproduce → production code |
| Add field to entity | Entity → `AppDbContext` → DTO → Command/Query → Controller |
| Refactor service | Interface in `Application/Interfaces/` → Implementation in `Infrastructure/Services/` → test file |
