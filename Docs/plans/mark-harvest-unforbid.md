# Mark Harvest + Unforbid

**Implementation branch:** `codex/mark-harvest-unforbid`
**Implementation worktree:** `C:\dev\RimAI-worktrees\mark-harvest-unforbid`

## Goal

Add player-confirmed Assisted Apply for Food's two simplest concrete actions:
marking safe plant targets for harvest and unforbidding known food stacks.

This remains assisted gameplay, not Auto. The player clicks Apply on one
backend-approved advice step; RimAI validates fresh game state, executes exactly
one allowlisted non-pawn RIMAPI write, refreshes state, and reports the result.

## Decisions

- `mark_harvest` uses existing RIMAPI `POST /api/v1/order/designate/area`.
- `unforbid` requires a safe RIMAPI endpoint; do not use
  `/api/v1/map/destroy/forbidden`.
- Apply metadata is created only by deterministic backend/rules code.
  LLM-normalized steps never become directly executable.
- The dashboard sends `POST /api/advice/{adviceId}/steps/{stepIndex}/apply`
  with no target payload. Host looks up the active `AdviceItem` and step from
  `AdviceBus`.
- Apply buttons appear only when the step has an exact, bounded, revalidatable
  target. Otherwise the advice remains text-only.

## Phase 0 - Worktree And Plan Artifact

- Implement this feature in a dedicated worktree at
  `C:\dev\RimAI-worktrees\mark-harvest-unforbid`; do not continue the feature
  work in the dirty primary checkout.
- Create the worktree from current `master` with branch
  `codex/mark-harvest-unforbid`.
- Keep `C:\dev\RimAI` and any existing Codex worktrees intact; do not force
  checkout `master` in a worktree where Git says it is already checked out
  elsewhere.
- Save this plan as `Docs/plans/mark-harvest-unforbid.md` and copy/commit it
  from the feature worktree as the first feature artifact.
- Stage only files belonging to this feature from the feature worktree.
- Rename the roadmap/docs slice to "Mark Harvest + Unforbid" rather than an
  M-number label.

## Phase 1 - RIMAPI Write Surface

- Verify the live `order/designate/area` request shape against RIMAPI docs/live
  smoke before wiring harvest.
- Add a companion RIMAPI endpoint for safe unforbid, e.g.
  `POST /api/v1/order/unforbid`, scoped to map item ids.
- The unforbid endpoint rejects empty batches, missing targets, non-map things,
  and oversized batches; it returns requested/matched/changed/already-allowed
  and missing counts.
- Update cached RIMAPI docs after the endpoint exists and is visible via
  `/api/v1/dev/endpoints`.

## Phase 2 - RimAI Contracts

- Add optional `apply` metadata to `AdviceStep`.
- Minimal apply handle fields: action kind, button label, target summary, and
  opaque target data needed by Host validation.
- Supported kinds for this slice:
  - `mark_harvest_area`
  - `unforbid_things`
- Do not expose raw RIMAPI path/body in the dashboard contract.
- Add frontend type mirrors for the new optional `apply` object.

## Phase 3 - Food Target Derivation

- Extend Food briefing derivation with compact executable target data:
  - forbidden known food stack ids/defs/counts/positions for `unforbid`
  - harvest rects for crop or wild plant clusters only when positions and
    readiness are precise enough
- Preserve compact briefing style: do not dump every plant or item into prompts.
- If harvest readiness cannot be tied to exact plant positions or zone/cell
  geometry, do not attach an apply handle.
- Add Food rule logic that attaches apply metadata to `Unforbid` and
  `MarkHarvest` steps only for eligible targets.

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
- Record latest apply attempts in bounded Host memory and expose them through
  `/api/system/health`.

## Phase 5 - Dashboard Apply UI

- Add an apply API helper and hook with per-advice-step pending/result/error
  state.
- Render a compact Apply button beside eligible steps in the Advice view.
- Disable while pending and after successful apply in that browser session.
- Show the returned result message inline with the step.
- Keep non-eligible steps rendered exactly as advice, without broad write
  controls.

## Phase 6 - Tests

- RIMAPI: endpoint tests/manual smoke for forbidden stack changed to allowed
  without deletion.
- `RimApiClient`: payload tests for harvest designation and unforbid.
- Food derivation: executable targets are present only when exact ids/rects are
  available.
- Food rules: eligible `Unforbid` and `MarkHarvest` steps carry apply metadata;
  ineligible steps do not.
- Host endpoint: stale advice, missing target, oversized rect/batch, RIMAPI
  unavailable, RIMAPI rejection, success, and already-satisfied paths.
- Dashboard: build passes and Apply states render without layout shift.

## Acceptance Criteria

- A Food advice card can apply harvest for a safe target and report the result.
- A Food advice card can unforbid known food stacks once the RIMAPI endpoint is
  present.
- No LLM-supplied action becomes executable by itself.
- No pawn allocation, bills, schedules, medical/prisoner actions, combat
  controls, broad zone editing, or autonomous execution is introduced.
- `/api/system/health` shows recent Assisted Apply attempts.
- Sources to verify during implementation: [RIMAPI repo](https://github.com/IlyaChichkov/RIMAPI)
  and [RIMAPI API docs](https://ilyachichkov.github.io/RIMAPI/api.html).
