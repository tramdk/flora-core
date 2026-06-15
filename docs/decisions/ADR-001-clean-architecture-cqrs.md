# ADR-001: Clean Architecture + CQRS Pattern

| Field | Value |
| --- | --- |
| **Status** | accepted |
| **Date** | 2024-01-01 |
| **Affected** | All Application layer, Controllers, Infrastructure |

## Context

FloraCore is a blog/e-commerce API backend requiring clear separation between
read and write operations, testability via DI, and support for both EF Core
(for commands) and Dapper (for high-performance queries).

## Decision

Adopt **Clean Architecture** with **CQRS** (Command Query Responsibility
Segregation) using **MediatR** as the mediator.

### Key choices:
- **Domain ← Application ← Infrastructure ← Controllers** dependency direction.
- Each feature has its own vertical slice under `Application/Features/{Feature}/`.
- Commands (writes) use EF Core with LINQ.
- Queries (reads) use Dapper for performance.
- DTOs live in `Application/Features/{Feature}/DTOs/` — not in Commands/Queries folders.
- Controllers are thin — delegate to MediatR immediately.

## Consequences

### Positive
- Each feature is self-contained and easy to test in isolation.
- Read/write paths can be optimized independently.
- New features follow a predictable structure.

### Negative
- More files per feature (Command, Handler, Query, Handler, DTO, Validator).
- Developers must understand the pattern before contributing.

### Risks
- Over-abstraction for simple CRUD — mitigated by allowing tiny lane direct patches.

## References
- `CODING_POLICY.md` sections A–D
- `FloraCore.Tests/ArchitectureTests/`
