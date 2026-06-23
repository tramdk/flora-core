# Story: [ID] — [Title]

| Field | Value |
| --- | --- |
| **Lane** | tiny / normal / high-risk |
| **Status** | draft / in_progress / done / blocked |
| **Feature** | Feature slice name (e.g., Products, Orders) |
| **Created** | YYYY-MM-DD |

## Goal

One sentence: what must be true when this story is done?

## Acceptance Criteria

- [ ] Criterion 1
- [ ] Criterion 2
- [ ] Criterion 3

## Affected Files

| Layer | Files |
| --- | --- |
| Domain | `Domain/Entities/...` |
| Application | `Application/Features/{Feature}/Commands/...` |
| Infrastructure | `Infrastructure/Repositories/...` |
| Controller | `Controllers/...Controller.cs` |
| Tests | `FloraCore.Tests/Application/Features/{Feature}/...` |

## Risk Checklist

- [ ] DB schema / migration
- [ ] Auth / security boundary
- [ ] Payment / order logic
- [ ] Public API contract change
- [ ] New external service
- [ ] Multiple feature slices

## Validation Plan

| Test level | What to prove |
| --- | --- |
| Unit | Handler returns correct result for happy/edge/fail paths |
| Integration | (if applicable) |
| E2E | (if applicable) |
| Manual | (if applicable) |

## Decision References

- ADR-NNN (if any architectural decision was made)

## Notes

Additional context, links, or implementation notes.
