# AGENTS.md — Agent Operating Rules

## UNIVERSAL RULES (Always Apply)

### Autonomous Execution

- **End-to-End in one turn**: Execute all phases (Discovery → Coding → Verification → Final Checks) in a **single response** without pausing for user prompts.
- **Only pause when**: (1) Large design needs approval (Implementation Plan), or (2) Critical business logic ambiguity that cannot be self-resolved.
- **Complete delivery**: Return fully built, tested, and checked results.

### Discovery

1. **Feature Intake Gate**: Before performing any code changes, complete the intake gate flow according to [FEATURE_INTAKE.md](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/FEATURE_INTAKE.md). Classify risk (Tiny, Normal, High-Risk) and follow the lane routing. Create a story packet in `docs/stories/` based on [story template](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/templates/story.md) if required.
2. **Context Engineering**: Follow the context rules specified in [CONTEXT_RULES.md](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/CONTEXT_RULES.md). Match your reads/writes dynamically to the Phase × Risk lane matrix.
3. **Architecture Decisions (ADRs)**: If making significant design decisions, consult existing records in [decisions](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/decisions/README.md) and record new ones using the [decision template](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/templates/decision.md).
4. **CodeGraph first**: Run `codegraph sync` at session start. Use `codegraph` MCP tools to locate symbols and trace references before reading files.
5. **Scan Skills**: Check `<skills>` list. If a relevant skill exists, read its `SKILL.md` before proceeding.
6. **Test Matrix**: Update the [TEST_MATRIX.md](file:///c:/Users/T/.gemini/antigravity/scratch/flora-core/docs/TEST_MATRIX.md) when adding/modifying behaviors or verification tests.

### Version Control

- **No commits**: Only use `git status`, `git diff`, `git add`. Never `git commit`.

### Session End

- Run `codegraph sync` before handoff.

### Token Optimization

- **Always Active**: Always apply the rules of `caveman` (full) and `ponytail` (full) skills by default for all responses to optimize token consumption and minimize output length.

---

## C#/.NET RULES (Apply ONLY when the task involves `.cs` files or .NET projects)

> Skip this entire section if the task does not involve C#/.NET code.

### Phase 1: Discovery (C#)

1. Read the corresponding test file in `FloraCore.Tests/` before writing code to understand Method Signatures and Expected Behavior.
2. **TDD — Write tests first**: Create new test cases before implementing logic. Ensure they compile and fail (Red Light).

### Phase 2: Coding (C#)

3. **TDD Cycle (Red → Green → Refactor)**:
   - **Red**: `dotnet test --filter <test_name>` — confirm new test fails.
   - **Green**: Modify **one** `.cs` file at a time with minimal code. Run `dotnet build FloraCore.csproj` then re-run test to confirm pass.
   - **Refactor**: Optimize structure while keeping tests green.
4. **Follow** `CODING_POLICY.md` **100%**, key points:
   - Stay within User Story scope — no feature creep.
   - 100% test coverage: Happy Path + Edge Case + Exception/Fail Path.
   - Primary Constructors (C# 12+) with null checks for DI.
   - CQRS & MediatR: immutable record Commands/Queries, independent Handlers with XML docs.
   - `AsNoTracking` for read-only queries; pagination & filtering support.
   - `async/await` with `CancellationToken` for all I/O.
   - EF Core (LINQ) for Commands, Dapper for high-performance read Queries.
   - `IOptions<T>` for config; `IResourceManager` for error localization.
   - DTOs in dedicated `DTOs/` folder under each Feature Slice.
   - No hardcoded secrets — use `.env` environment variables.
   - SSOT: centralize business rules in one place.
   - Spec-Driven: sync API contract via `Specs/openapi.json` and `ApiContractTests.cs`.

### Phase 3: Verification (C#)

5. **Compiler errors**: On CS1503/CS1061, cross-check the source interface/class. After 3 failures, read the test file for the correct signature.
6. **TestWriter role**: Only edit files in `FloraCore.Tests/`. Never touch production code.
7. Run `dotnet test` (full or filtered) to confirm reliability.

### Phase 4: Final Checks (C#)

8. Run `./scripts/final-check.ps1 validate-all` — must pass 100% before completion.