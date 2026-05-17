# Mark Harvest + Unforbid

**Implementation branch:** `codex/mark-harvest-unforbid-worktree`
**Implementation worktree:** `C:\dev\RimBob-worktrees\mark-harvest-unforbid`

## Goal

Add player-confirmed Assisted Apply for Food's two simplest concrete actions:
marking safe plant targets for harvest and unforbidding known food stacks.

This remains assisted gameplay, not Auto. The player clicks Apply on one
backend-approved advice step; RimBob validates fresh game state, executes exactly
one allowlisted non-pawn RIMAPI write, refreshes state, and reports the result.

## Decisions

- `mark_harvest` uses existing RIMAPI `POST /api/v1/order/designate/area`.
- `unforbid` requires a safe RIMAPI endpoint; do not use
  `/api/v1/map/destroy/forbidden`.
- Apply metadata is created only by deterministic backend/rules code.
  LLM-normalized actions never become directly executable.
- The dashboard sends `POST /api/advice/{adviceId}/actions/{actionIndex}/apply`
  with no target payload. Host looks up the active `AdviceItem` and action from
  `AdviceBus`.
- Apply buttons appear only when the step has an exact, bounded, revalidatable
  target. Otherwise the advice remains text-only.

## Phase 0 - Worktree And Plan Artifact

- Implement this feature in the dedicated worktree at
  `C:\dev\RimBob-worktrees\mark-harvest-unforbid`; do not continue the feature
  work in the dirty primary checkout.
- Use branch `codex/mark-harvest-unforbid-worktree`, created from current
  `master`.
- Leave the older `codex/mark-harvest-unforbid` branch untouched unless a
  human explicitly asks to clean it up; it diverged from current `master`.
- Keep `C:\dev\RimBob` and any existing Codex worktrees intact; do not force
  checkout `master` in a worktree where Git says it is already checked out
  elsewhere.
- Save this plan as `Docs/plans/mark-harvest-unforbid.md` and commit it from
  the feature worktree as the first feature artifact.
- Stage only files belonging to this feature from the feature worktree.
- Rename the roadmap/docs slice to "Mark Harvest + Unforbid" rather than an
  M-number label.

## Phase 1 - RIMAPI Write Surface

- Verify the live `order/designate/area` request shape against RIMAPI docs/live
  smoke before wiring harvest.
- Add a companion RIMAPI endpoint for safe unforbid in the sibling fork repo at
  `C:\dev\RIMAPI-for-RimBob` (`ocaspi-sc/RIMAPI-for-RimBob`), not in RimBob
  Host. Use a separate RIMAPI branch for that change. If the fork repo is not
  available locally, stop `unforbid` implementation after the RimBob-side
  contract/client/test scaffolding and leave it blocked on the explicit fork
  endpoint dependency.
- Proposed RIMAPI endpoint: `POST /api/v1/order/unforbid`, scoped to map item
  ids.
- The unforbid endpoint rejects empty batches, missing targets, non-map things,
  and oversized batches; it returns requested/matched/changed/already-allowed
  and missing counts.
- Update cached RIMAPI docs after the endpoint exists and is visible via
  `/api/v1/dev/endpoints`.

## Phase 2 - RimBob Contracts

- Add optional `apply` metadata to `AdviceAction`.
- Minimal apply handle fields: `kind`, `label`, `target_summary`, and opaque
  target data needed by Host validation.
- Supported kinds for this slice:
  - `mark_harvest_area`
  - `unforbid_things`
- Do not expose raw RIMAPI path/body in the dashboard contract.
- Treat `apply` as server-owned. `AdviceActionNormalizer`, manual LLM ingestion,
  and any raw model parsing path must ignore or strip model-supplied `apply`
  fields.
- Define the apply response contract before frontend/backend parallel work:
  `status`, `message`, `kind`, `advice_id`, `action_index`, and optional
  `readback`.
- Add frontend type mirrors for the new optional `apply` object.

## Phase 3 - Food Target Derivation

- Extend Food briefing derivation with compact executable target data:
  - forbidden known food stack ids/defs/counts/positions for `unforbid`
  - harvest rects for crop or wild plant clusters only when positions and
    readiness are precise enough
- Preserve compact briefing style: do not dump every plant or item into prompts.
- `unforbid` targets come from current forbidden food-like `ThingRecord` /
  `StoredResourceRecord` rows with stable ids, defs, counts, and map positions.
  Do not attach an apply handle for category-only or position-only summary text.
- Crop harvest targets use growing-zone cells when available; otherwise use
  exact ready plant positions only when they form a small bounded rectangle.
- Wild harvest targets use exact ready wild plant positions only when they form
  a small bounded rectangle near the selected Food reference point.
- Define and enforce caps in code/tests before wiring the button: maximum target
  count, maximum rect area, and maximum missing-target tolerance after refresh.
- If harvest readiness cannot be tied to exact plant positions or zone/cell
  geometry, do not attach an apply handle.
- Add Food rule logic that attaches apply metadata to `Unforbid` and
  `MarkHarvest` actions only for eligible targets.

## Phase 4 - Host Apply Pipeline

- Add `AssistedApplyService`.
- Revalidate on every click:
  - advice id exists and is active
  - step index matches
  - step has apply metadata
  - target still exists and still needs the operation
  - operation is in the allowlist
- Execute one write:
  - harvest calls `RimApiClient.DesignateAreaAsync(..., Harvest, rect)`
  - unforbid calls `RimApiClient.UnforbidThingsAsync(...)`
- Refresh ingestion after the write.
- Return a structured result: `applied`, `already_satisfied`, `stale_advice`,
  `validation_failed`, `rimapi_unavailable`, `rimapi_rejected`, or
  `readback_inconclusive`.
- Response body shape is stable for all statuses: `status`, `message`, `kind`,
  `advice_id`, `action_index`, and optional `readback`.
- Record latest apply attempts in bounded Host memory and expose them through
  `/api/system/health`.

## Phase 5 - Dashboard Apply UI

- Add an apply API helper and hook with per-advice-step pending/result/error
  state.
- Render a compact Apply button beside eligible actions in the Advice view.
- Disable while pending and after successful apply in that browser session.
- Show the returned result message inline with the step.
- Extend SYSTEM health types/rendering so latest Assisted Apply attempts are
  visible in the dashboard, not just present in the backend JSON.
- Keep non-eligible actions rendered exactly as advice, without broad write
  controls.

## Phase 6 - Tests

- RIMAPI: endpoint tests/manual smoke for forbidden stack changed to allowed
  without deletion.
- `RimApiClient`: payload tests for harvest designation and unforbid.
- Food derivation: executable targets are present only when exact ids/rects are
  available.
- Food rules: eligible `Unforbid` and `MarkHarvest` actions carry apply metadata;
  ineligible actions do not.
- LLM/manual ingestion: model-supplied `apply` fields are ignored or stripped,
  and normalized LLM actions are never executable by themselves.
- Host endpoint: stale advice, missing target, oversized rect/batch, RIMAPI
  unavailable, RIMAPI rejection, success, and already-satisfied paths.
- Dashboard: build passes, Apply states render without layout shift, and SYSTEM
  displays latest Assisted Apply attempt metadata.

## Acceptance Criteria

- A Food advice card can apply harvest for a safe target and report the result.
- A Food advice card can unforbid known food stacks once the RIMAPI endpoint is
  present.
- If the RIMAPI unforbid endpoint is unavailable, harvest still ships and
  `unforbid` remains visibly blocked on that endpoint dependency rather than
  using an unsafe workaround.
- No LLM-supplied action becomes executable by itself.
- No pawn allocation, bills, schedules, medical/prisoner actions, combat
  controls, broad zone editing, or autonomous execution is introduced.
- `/api/system/health` shows recent Assisted Apply attempts.
- Sources to verify during implementation: local fork
  `C:\dev\RIMAPI-for-RimBob`, [fork repo](https://github.com/ocaspi-sc/RIMAPI-for-RimBob),
  upstream [RIMAPI repo](https://github.com/IlyaChichkov/RIMAPI), and
  [RIMAPI API docs](https://ilyachichkov.github.io/RIMAPI/api.html).
