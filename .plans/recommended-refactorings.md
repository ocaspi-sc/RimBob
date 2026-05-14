# RimAI C# Refactoring Plan

## Goal

Turn the codebase review recommendations into a safe, ordered refactoring sequence. The plan keeps behavior stable, preserves the suggest-only MVP boundary, and avoids broad reshaping before the next targeted cleanup is ready.

## Current status

As of 2026-05-15, the original advice-schema blocker is cleared.

Verified state:

- Worktree was clean before verification, except later unrelated `HumanTodo.md` edits.
- `rg` finds no live `AdviceSeverity` / `AdvicePriorityScore` references. Remaining `PriorityScore` text is legacy compatibility parsing/test naming.
- `dotnet test Src\Tests\RimAI.Tests.csproj --no-restore --no-build` passed: 120/120.
- `dotnet build Src\RimAI.sln --no-restore` compiled through `RimAI.Tests`, then failed only because a live `RimAI.Host` process locked ApiHost output DLLs.

Operational note:

- Stop the running Host before a full solution build. DLL-lock failures such as `MSB3021` / `MSB3027` are not evidence that the schema blocker returned.

## Phase 0 - Baseline and scope lock

Status: complete for the advice-schema blocker.

1. Record current failure with `dotnet build Src\RimAI.sln --no-restore`.
2. Search for old schema names:
   - `AdviceSeverity`
   - `PriorityScore`
   - `AdvicePriorityScore`
   - `severity` / `priority_score` in LLM fixtures and prompts
3. Confirm no unrelated dirty files need to be touched.
4. Keep the first PR/slice limited to schema repair.

Acceptance:

- Build failure is understood and listed.
- The first implementation slice has a narrow file list.

## Phase 1 - Finish the advice priority schema

Status: complete.

Decision to make:

- Either `AdvicePriority` fully replaces severity plus numeric score, or numeric rank remains a separate field.

Recommended direction:

- Keep `AdvicePriority` as the semantic tier.
- Defer numeric score unless there is a concrete dashboard arbitration need in the current slice.

Implementation tasks:

1. Update `AdviceBus.SortAdvice` to sort by `AdviceItem.Priority`, then `IssuedAt`.
2. Update Food rules to emit only `AdvicePriority`.
3. Update LLM parsing and normalization to accept legacy `severity` / `priority_score` input but output the current `AdviceItem` schema.
4. Update `ResourceRequest.Priority` mapping to use `AdvicePriority`.
5. Update tests and fixtures around Food rules, LLM parser, and advice bus sorting.
6. Update prompts only if they still instruct models to emit old fields.

Verification:

- `dotnet test Src\Tests\RimAI.Tests.csproj --no-restore --no-build` passed on 2026-05-15.
- `dotnet build Src\RimAI.sln --no-restore` reached the Host copy step and was blocked only by a running `RimAI.Host` process locking output DLLs.
- Rerun full build after stopping Host when a clean full-build signal is needed.

## Phase 2 - Extract LLM advice normalization

Status: next recommended refactor.

Problem:

`LlmAdviceResponseNormalizer` is doing too many jobs: tolerant JSON repair, strict validation, resource request normalization, suggested action repair, work-type inference, citation filtering, and default routing.

Target shape:

- `AdviceResponseNormalizer`
- `ResourceRequestNormalizer`
- `SuggestedActionNormalizer`
- `WorkTypeInference`
- `AdviceJsonCompatibility` for legacy model field aliases

Implementation notes:

1. Move one helper group at a time.
2. Preserve current tolerant parsing behavior.
3. Add focused tests for each extracted class before deleting old helpers.
4. Keep `FoodLlmResponseParser` as the Food-specific entry point.

Acceptance:

- Parser behavior stays covered by existing LLM tests.
- The main normalizer reads as orchestration rather than a bag of parsing rules.

## Phase 3 - Add a minister registry

Problem:

Minister metadata and lookup logic is spread across Host DI, cabinet orchestration, endpoint readiness, prompt introspection, and manual trigger behavior.

Current duplicated surfaces:

- `Src/ApiHost/Program.cs`
- `Src/Coordination/CabinetCycle.cs`
- `Src/ApiHost/Endpoints/MinisterEndpoints.cs`
- `Src/ApiHost/Endpoints/SystemEndpoints.cs`

Target shape:

- `MinisterRegistry`
- `MinisterDescriptor`
- Optional per-minister capability delegates:
  - prompt builder
  - briefing provider
  - manual trigger support
  - raw LLM output support
  - RAG retrieval support

Implementation order:

1. Introduce descriptors for Mayor and Food only.
2. Keep planned ministers as descriptors with `Ready = false`.
3. Make `CabinetCycle` resolve wired ministers through the registry.
4. Make `/api/ministers` return registry data.
5. Replace hard-coded `if (scope.Key == "food")` branches incrementally.

Acceptance:

- Adding Construction later requires a descriptor and minister registration, not edits in every endpoint.

## Phase 4 - Centralize endpoint coverage metadata

Problem:

`/api/system/health` has a manually maintained `endpoint_coverage` array that can drift from actual endpoint mappings.

Target shape:

- `EndpointCatalog`
- Each endpoint module registers coverage rows beside route mapping.
- `SystemEndpoints` projects catalog state instead of owning the list.

Implementation notes:

1. Start with static catalog entries.
2. Do not over-engineer route reflection.
3. Keep dashboard response shape stable.

Acceptance:

- Endpoint coverage rows are no longer hand-maintained inside `SystemEndpoints`.

## Phase 5 - Generalize RAG retrieval

Problem:

Mayor and Food RAG retrievers duplicate enablement checks, embedding, top-K retrieval, citation creation, and snippet truncation.

Target shape:

- `RagRetriever<TBriefing>`
- `RetrievalProfile`
- Per-minister query builders:
  - `MayorRagQueryBuilder`
  - `FoodRagQueryBuilder`

Implementation order:

1. Expand `RetrievalProfile` to include name, citation prefix, top-K, snippet length.
2. Extract shared retrieval flow.
3. Keep current query text output unchanged.
4. Verify Mayor and Food RAG tests still pass.

Acceptance:

- A new minister can add RAG by defining a query builder and profile.

## Phase 6 - Split ingestion mapping from orchestration

Problem:

`IngestionDispatcher` currently performs RIMAPI fan-out, aggregate mapping, and state writes in one class.

Target shape:

- `IngestionDispatcher` orchestrates endpoint calls and state updates.
- DTO mapping moves into small pure mappers:
  - `PawnAggregateMapper`
  - `MapAggregateMapper`
  - `ResourceAggregateMapper`
  - `ThreatAggregateMapper`

Implementation notes:

1. Extract pure methods without changing DTO or aggregate records.
2. Add mapper tests only where logic is non-trivial.
3. Use the existing `IngestionDispatcherTests` as regression coverage.

Acceptance:

- Dispatcher reads as a refresh workflow.
- Mapping behavior remains independently testable.

## Phase 7 - Extract shared derivation helpers

Problem:

Mayor and Food derivations both compute living pawns, season context, skill summaries, threat heuristics, building categories, and distance/proximity concepts.

Target shape:

- `StateStore/Derivations/Common/SeasonDeriver`
- `PawnDeriver`
- `ThreatDeriver`
- `BuildingClassifier`
- `MapDistance`

Implementation notes:

1. Extract only duplicated or clearly reusable logic.
2. Do not move minister-specific briefing language into common helpers.
3. Keep briefings compact and domain-owned.

Acceptance:

- Mayor and Food derivations are shorter without losing readability.
- Common helpers are still pure and dependency-free.

## Phase 8 - Generalize briefing cache entries

Problem:

`BriefingCache` has repeated cache/recompute/version/logging flow for each briefing type. This will get noisier as Construction and Defense arrive.

Target shape:

- `CachedBriefing<TBriefing>`
- `BriefingCache` owns named cache entries and exposes typed methods.

Implementation notes:

1. Keep public methods `GetMayorBriefing()` and `GetFoodBriefing()`.
2. Extract the repeated version comparison and debug logging.
3. Add tests that existing cache invalidation semantics remain unchanged.

Acceptance:

- Adding a new briefing does not require duplicating the full cache pattern.

## Guardrails

- Keep `RimAI.Core` pure.
- Keep MVP suggest-only; do not add RIMAPI writes during these refactors.
- Do not introduce planner, Labor solver, or Auto execution.
- Prefer one refactor phase per commit/slice.
- Run build/tests after each phase.
- Update design docs only when a refactor changes a durable architecture convention.

## Suggested execution order

1. Phase 2 - LLM normalization split.
2. Phase 3 - minister registry.
3. Phase 4 - endpoint coverage catalog.
4. Phase 5 - RAG retriever generalization.
5. Phase 6 - ingestion mapper extraction.
6. Phase 7 - derivation helpers.
7. Phase 8 - briefing cache generalization.

Completed:

- Phase 1 - advice schema repair.
