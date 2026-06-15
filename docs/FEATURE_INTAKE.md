# Feature Intake

Every implementation request enters the intake gate before code changes.
The agent classifies risk — the human does not need to.

## Intake Flow

```text
User prompt
    |
    v
Classify input type
    |
    v
Restate as work item
    |
    v
Find affected product docs, controllers, and stories
    |
    v
Run risk checklist
    |
    v
Choose lane: tiny, normal, or high-risk
```

## Input Types

| Type | Use when | Typical artifact |
| --- | --- | --- |
| Bug fix | Fixing broken behavior that has passing tests or user reports | Direct patch + update test |
| Change request | Changing, fixing, or refining accepted behavior | Story packet or direct patch |
| New feature | Adding a new Command/Query/Controller endpoint | Story packet |
| DB migration | Adding/changing entities, relationships, or EF migrations | Story packet + high-risk review |
| Integration | Connecting external services (AI, Payment, Telegram, Zalo) | Story packet + decision record |
| Refactor | Restructuring code without changing behavior | Direct patch + verify tests pass |
| Docs/config | Updating docs, settings, scripts, or non-code files | Direct patch |

## Risk Checklist

Before choosing a lane, check these escalation triggers:

- [ ] Does the change touch database schema or migrations?
- [ ] Does it affect authentication, authorization, or security?
- [ ] Does it modify payment or order processing logic?
- [ ] Does it change a public API contract (`openapi.json`)?
- [ ] Does it introduce a new external service dependency?
- [ ] Does it affect more than 3 feature slices?

**0 checks → tiny or normal. 1–2 checks → normal. 3+ checks → high-risk.**

## Lanes

### Tiny

Use for: docs, config, copy fixes, renaming, single-line bug fixes, adding a field to a DTO without DB change.

Requirements:
- Patch directly — no story packet needed.
- Keep affected docs current.
- Run `dotnet build` to verify.
- Run existing tests if touching production code.

### Normal

Use for: new CQRS Command/Query, new Controller endpoint, adding test coverage, refactoring a service.

Requirements:
- Record as a work item (story in `docs/stories/` if multi-step).
- Follow full TDD cycle (Red → Green → Refactor).
- Run `dotnet test` — all tests must pass.
- Update `Specs/openapi.json` if API surface changes.
- Update `docs/TEST_MATRIX.md` if adding new behavior.
- Run `./scripts/final-check.ps1 validate-all`.

### High-Risk

Use for: DB migrations, auth changes, payment logic, multi-feature refactors, new external integrations.

Requirements:
- **Must** create a story packet in `docs/stories/`.
- **Must** create or update a decision record in `docs/decisions/` for architectural choices.
- **Must** review `docs/ARCHITECTURE.md` or `CODING_POLICY.md` for boundary rules.
- Follow full TDD cycle with all 3 scenarios: Happy Path + Edge Case + Fail Path.
- Run `dotnet test` — 100% pass.
- Update `Specs/openapi.json` and `docs/TEST_MATRIX.md`.
- Run `./scripts/final-check.ps1 validate-all`.
- Report impact summary to user before proceeding.

## Flora-Core Specific Risk Domains

| Domain | Default lane | Escalation trigger |
| --- | --- | --- |
| Products / ProductCategories | Normal | DB schema change → High-risk |
| Posts / PostCategories | Normal | Crawler logic change → High-risk |
| Orders / Cart / Payments | High-risk | Always — money involved |
| Auth / Users | High-risk | Always — security boundary |
| Chat / Notifications | Normal | Real-time hub change → High-risk |
| WebsiteInfo | Tiny–Normal | Rarely high-risk |
| Favorites / Reviews | Tiny–Normal | Rarely high-risk |
| Telegram/Zalo bots | Normal | External API change → High-risk |
