# Plan: Right-sidebar LOG tab — curated "most important events" feed

## Motivation

User request (verbatim): *"dashboard: add a new tab to the right side-panel: LOG. It will only log the most important events: Actions applied, Cabinet runs, escalations. you can suggest a couple more. One small card per item, using a title + icons/emojis with tooltips"*

Today the right sidebar ([ColonySidebar.tsx](Dashboard/src/components/layout/ColonySidebar.tsx)) is a single always-on `aside` showing colony facts + colonist cards, independent of the selected scope. The operator can already see "events" today, but only by navigating into the **SYSTEM** scope's Events tab, which shows the full raw `DashboardEvent` timeline (cap 80, every SSE connect/ping/parse_error) plus the Assisted Apply attempt table. That surface is a debug firehose buried one scope away.

The ask is a second, **curated, always-visible** surface in the sidebar: only the handful of events that matter during play — actions the player applied, cabinet runs, and minister escalations — as small icon-led cards. It stays visible no matter which scope is selected, so the operator does not lose the running "what just happened" log when they open a minister inspector.

## Key finding: this is frontend-only, no backend work

All required event data already reaches the client through inputs `App.tsx` already polls/streams:

- **Actions applied** — `systemHealth.data.assisted_apply.recent_attempts[]` (`AssistedApplyAttempt`: `at`, `status`, `message`, `kind`, `advice_id`, `action_index`), polled every 15s via [`fetchSystemHealth`](Dashboard/src/api/status.ts). This is the same backend-owned, deduped list the SYSTEM Events tab already renders ([SystemOverview.tsx:76](Dashboard/src/components/system/SystemOverview.tsx)).
- **Cabinet runs** — `feed.cabinetRuns` (`CabinetRunLogSnapshot[]`), streamed live over SSE `cabinet_run` and already kept (cap 12) in [adviceFeedReducer.ts](Dashboard/src/hooks/adviceFeedReducer.ts) (`mergeCabinetRuns`).
- **Escalations** — `systemHealth.data.traces[]` (`MinisterTrace`), each carrying `escalationReason`, `path` (`rules` / `llm` / `llm_failed`), `minister`, `completedAt`, `adviceCount`, `flagCount`. Polled with system health. This is the exact source the [MinisterEscalationCallout](Dashboard/src/components/minister/MinisterEscalationCallout.tsx) already classifies.

So the LOG tab is a **pure-frontend derivation** over already-available state: merge → classify → sort → cap → render. No new endpoint, no SSE event type, no persisted/wire shape change, no `App.tsx` data plumbing beyond passing existing values into the sidebar.

## Suggested extra event kinds (the "couple more")

The three named kinds plus, recommended:

- **LLM / provider outcome** — folds into the same `traces[]` source. A trace with `path: "llm"` and an `escalationReason` is "escalation **used** — LLM judgment shaped this output" (ok tone); `path: "llm_failed"` is "LLM path failed" (error tone); `path: "rules"` + `escalationReason` is "rules **requested** escalation, awaiting confirm" (warn tone). One source, three classifications — this is why "escalations" and "LLM outcome" are the same card family, not two.
- **New critical advice** — when a `critical`-priority `AdviceItem` enters the active feed. This is RimBob raising an alarm and is worth a one-line card. Source: the active advice feed (`feed.feed.activeAdvice`) — emit a card the first time a given critical advice `id` is seen this session.

Deferred candidates (named in the plan, not built in this slice, to keep it small):

- **Connectivity transitions** — RimWorld / RIMAPI / Host-API going offline↔online. Genuinely useful on a debug console ("why did everything go quiet"), but requires a small previous-vs-current transition tracker rather than a stateless derivation. Defer to a follow-up.
- **New Host build served** — `host_ready` with a changed `reload_token` (the dashboard already hard-reloads on this). Low marginal value once reload happens; defer.

Final core taxonomy for this slice: **Action applied · Cabinet run · Escalation/LLM · New critical advice.**

## Design

### Structure: sidebar gets a shallow 2-tab bar

The sidebar becomes a shallow tabbed surface inside the same `aside.colony-sidebar.panel-shell`:

- `COLONY` (default) — the current content (facts, resources, colonist cards), unchanged.
- `LOG` — the curated event-card list.

`COLONY` stays the default so the sidebar's current always-on colony behavior is preserved on load. Tab selection is local `useState` in the sidebar, default `'colony'` (kept ephemeral this slice — see Open decisions for optional persistence). Keep the tab layer shallow, matching the design doc's "shallow local tab bar" rule for console scopes; reuse the existing tab styling/markup idiom rather than inventing a new control.

The colony snapshot empty/error states (no snapshot yet, snapshot poll failed) currently early-return the whole `aside`. With tabs, those states must not blank out the LOG tab — restructure so the tab bar always renders, and only the COLONY tab body shows the snapshot empty/error state. The LOG tab renders from its own inputs and shows its own empty state ("No important events yet this session") when the derived list is empty.

### Data: one pure selector

Add `deriveLogEntries(...)` to the existing [selectors.ts](Dashboard/src/dashboard/selectors.ts) module (pure, no React), returning a sorted, capped `LogEntry[]`:

```ts
export type LogEntryKind = 'action_applied' | 'cabinet_run' | 'escalation' | 'critical_advice';
export type LogTone = 'ok' | 'info' | 'warn' | 'error';

export interface LogChip { emoji: string; label: string; tooltip: string }

export interface LogEntry {
  id: string;          // stable per source row (e.g. `apply:${advice_id}:${action_index}:${at}`, `cabinet:${run_id}`, `trace:${minister}:${completedAt}`, `advice:${id}`)
  at: string;          // ISO timestamp used for sort
  kind: LogEntryKind;
  tone: LogTone;
  title: string;       // one-line card title
  icon: string;        // leading kind emoji
  chips: LogChip[];    // small emoji chips, each with a hover tooltip
  tooltip: string;     // full detail for the card-level title attribute
}

export function deriveLogEntries(input: {
  applyAttempts: AssistedApplyAttempt[];
  cabinetRuns: CabinetRunLogSnapshot[];
  traces: MinisterTrace[];
  criticalAdvice: AdviceItem[];   // already-active advice filtered to priority === 'critical'
}): LogEntry[]
```

Behavior:

- **Merge** the four sources into one array, **sort by `at` descending**, **cap** at a small bound (~40 entries) so the sidebar stays compact.
- **Tone mapping**: applied/already_satisfied → `ok`; stale_advice/validation_failed → `warn`; rimapi_unavailable/rimapi_rejected/error → `error`. Cabinet `status: "failed"` → `error`, else `ok`/`info`. Trace: `rules`+reason → `warn`, `llm` → `ok`, `llm_failed` → `error`. Critical advice → `warn`.
- **Titles** read as outcomes, e.g. `Applied · Unforbid 57 packaged survival meals` (from `message`), `Cabinet run complete · 5 steps`, `🍲 Chef escalated to LLM`, `Critical · <advice title>`.
- **Chips** carry compact secondary facts with tooltips: minister emoji (`iconForScope(minister)`), status/path token, step count + duration for cabinet, advice/flag counts for traces.
- **Dedup**: escalations come **only** from canonical `traces[]`. Per-step `CabinetRunStepSnapshot.escalation_reason` is **not** separately logged — the cabinet-run card already represents that run, and the trace list already carries the escalation. This avoids double cards for one escalation.

Keeping the derivation a pure function (no React, no hooks) makes it unit-testable in isolation and keeps the card component dumb.

### Card: title + emoji chips + tooltips

One small `article.log-card.<tone>` per `LogEntry`:

- Leading kind emoji via the existing `SemanticLabel` / `iconForSection` infra (reuse `assisted_apply`, `cabinet`, `llm_escalation`, `advice` icon keys already in [semanticIcons.ts](Dashboard/src/dashboard/semanticIcons.ts)).
- Title text on one line that **wraps** (no truncation), matching the sidebar's text-wrap rule.
- A row of small emoji chips, each a `<span title={tooltip}>` so hovering explains the chip (minister, status, counts, duration).
- A relative/clock time and the full `entry.tooltip` on the card's `title` attribute.

Render via a new `SidebarLog` component (or inline section in `ColonySidebar`); it takes `LogEntry[]` and renders the list or the empty state. Tone classes mirror the existing `timeline-row {severity}` / `advice-card` / `minister-escalation-callout` tone palette so LOG reads consistently with the rest of the dark console. Add `.log-card*` CSS alongside the existing sidebar styles.

### Wiring in App.tsx

`ColonySidebar` already receives `snapshot`/`error`/`loadedAt`/`staleSnapshot`. Add the LOG inputs as props from values `App` already holds: `systemHealth.data` (for `assisted_apply.recent_attempts` + `traces`), `feed.cabinetRuns`, and the critical subset of `feed.feed.activeAdvice`. No new hooks, no new polling. Compute `criticalAdvice` inline at the call site (or pass `activeAdvice` and filter in the selector — pick one; filtering in the selector keeps the prop surface smaller).

## Why this is not redundant with SYSTEM Events / ANALYTICS

Call this out so review does not flag overlap:

- **SYSTEM › Events** is the raw operations firehose (full `DashboardEvent` timeline incl. SSE connect/ping/parse_error, the full Assisted Apply attempt table, raw traces) — debug-first, lives inside the SYSTEM scope you must navigate to.
- **ANALYTICS** is derived live *aggregates* (advice mix, SSE health rollups), not a per-event log.
- **Sidebar › LOG** is a **curated, capped, always-visible** subset of the *most important* discrete events, readable without leaving the current scope. Same underlying data, different job. (A later cleanup could have SYSTEM Events consume the same `deriveLogEntries` selector, but that is out of scope here.)

## Full subsume of the Cabinet Run dialog ("toast")

Decision: **LOG fully replaces the toast.** Delete `CabinetRunDialog` ([CabinetRunDialog.tsx](Dashboard/src/components/layout/CabinetRunDialog.tsx)) — the wide `aria-modal="false"` overlay that auto-pops on `Run Cabinet Now` and shows one run's step tree (incl. the landed Willie solver subitems from `.plans/cabinet-toast-solver-log.md`, `CabinetRunStepSnapshot.children` rendered by a recursive `CabinetRunStepRow`) — and render cabinet runs entirely inside the LOG tab. `cabinet-toast-overflow` has since landed (`ce2a9b4`) containing the toast's text overflow; that containment moves into the sidebar rendering rather than staying on a separate overlay.

The cabinet-run LOG card is an **expandable disclosure**, per the repo disclosure convention (real `<button>` header with `aria-expanded`/`aria-controls` + a conditionally rendered panel in normal flow):

- **Collapsed** = the one-line summary (status tone, step count, source ministers, duration), like every other LOG card.
- **Expanded** = full run detail: a small header line (short `run_id`, started time, duration, `state_source`/restored-snapshot chips) plus the `<ol>` of steps rendered by the **recursive `CabinetRunStepRow`**, so the landed Willie solver `children` subitems render unchanged.

Reuse, not rewrite: extract `CabinetRunStepRow` + `compactTraceSummary` (and the shared `formatDuration`/`formatTime`) out of `CabinetRunDialog.tsx` into a shared `Dashboard/src/components/layout/CabinetRunSteps.tsx` consumed by the LOG card, then delete `CabinetRunDialog.tsx`. No backend change — the same `cabinet_run` SSE snapshot already carries everything.

**Live feedback without the modal.** On a fresh `Run Cabinet Now` / `Run Cabinet (Rules Only)` click the sidebar auto-switches to the LOG tab and the active run's card auto-expands while `status === 'running'`, so the operator watches progress live exactly as the popup did — docked in the always-on sidebar instead of a floating overlay. Auto-expand releases (the card collapses to its summary) once the run completes, or when the operator manually toggles it.

Plumbing:

- [useManualTriggers.ts](Dashboard/src/hooks/useManualTriggers.ts) — drop the dialog open-state (`cabinetRunDialog`, `closeCabinetRunDialog`). Keep the optimistic seed (`seedCabinetRun`/`failCabinetRun`) and `activeCabinetRunIdRef` reconcile, but expose them as data: `activeCabinetRun: CabinetRunLogSnapshot | null` and `activeCabinetRunId: string | null`. This preserves instant-on feedback before the first `cabinet_run` SSE/POST result and still surfaces POST failures as a failed run.
- [App.tsx](Dashboard/src/App.tsx) — remove the `<CabinetRunDialog>` render. Merge the optimistic `activeCabinetRun` into `feed.cabinetRuns` by `run_id` (the seeded row is replaced once the real SSE snapshot lands) and pass the merged list + `activeCabinetRunId` into `ColonySidebar`.
- [ColonySidebar.tsx](Dashboard/src/components/layout/ColonySidebar.tsx) / `SidebarLog` — a `useEffect` switches the sidebar tab to `log` and marks the active run expanded whenever `activeCabinetRunId` changes to a new non-null id; otherwise each cabinet card owns its own expand toggle.

Narrow-column containment is now **mandatory** — the step tree lives in the sidebar, the narrowest region. Port the `cabinet-toast-overflow` text-containment rules (`ce2a9b4`) into the `.log-card`/step CSS so long detail/trace text wraps or clamps and the nested tree never overflows the sidebar. This is the one real cost of full subsume and is handled in the CSS slice, not deferred.

## Files

- [Dashboard/src/dashboard/selectors.ts](Dashboard/src/dashboard/selectors.ts) — add `LogEntry`/`LogChip`/`LogEntryKind`/`LogTone` types + `deriveLogEntries(...)` pure selector.
- [Dashboard/src/components/layout/ColonySidebar.tsx](Dashboard/src/components/layout/ColonySidebar.tsx) — add the COLONY|LOG tab bar; restructure so empty/error states scope to the COLONY tab only; render the LOG tab; auto-switch to LOG + auto-expand the active run when `activeCabinetRunId` changes (see toast section).
- New `Dashboard/src/components/layout/SidebarLog.tsx` (or an in-file section) — the LOG card list + empty state; the cabinet-run card is an expandable disclosure that renders the step tree inline.
- New `Dashboard/src/components/layout/CabinetRunSteps.tsx` — `CabinetRunStepRow` (recursive) + `compactTraceSummary` + shared formatters, extracted from the deleted dialog and consumed by the LOG cabinet card.
- **Delete** [Dashboard/src/components/layout/CabinetRunDialog.tsx](Dashboard/src/components/layout/CabinetRunDialog.tsx) — fully replaced by the LOG expandable card (no dead code per repo convention).
- Dashboard CSS (the existing global stylesheet the sidebar uses) — `.sidebar-tabs*` and `.log-card*` rules + the narrow-column step-tree containment ported from `cabinet-toast-overflow` (`ce2a9b4`), reusing existing tone tokens. Remove the `.cabinet-run-overlay`/`.cabinet-run-dialog` overlay rules; keep `.cabinet-run-step*`/`.cabinet-run-substeps` (now used inside the LOG card).
- [Dashboard/src/hooks/useManualTriggers.ts](Dashboard/src/hooks/useManualTriggers.ts) — drop `cabinetRunDialog`/`closeCabinetRunDialog`; expose `activeCabinetRun` + `activeCabinetRunId` (keep the optimistic seed + reconcile).
- [Dashboard/src/App.tsx](Dashboard/src/App.tsx) — remove the `<CabinetRunDialog>` render; merge `activeCabinetRun` into `feed.cabinetRuns` by `run_id`; pass the merged runs + `activeCabinetRunId` + `systemHealth.data` + critical advice into `ColonySidebar`.

## Out of scope / non-goals

- **No backend change.** No new endpoint, no new SSE event type, no change to `/api/system/health`, traces, or the apply contract. Pure frontend over existing inputs.
- **No new polling or hooks.** Reuse the existing `systemHealth` poll and the `cabinet_run` SSE feed. The 15s system-health cadence means applied/escalation cards can lag up to ~15s; acceptable for a log, and explicitly **not** worth adding a faster poll or threading an apply-callback event bus in this slice.
- **No connectivity / build-served cards** this slice (deferred candidates above).
- **No SYSTEM Events refactor** to share the selector (possible later cleanup, noted not done).
- **No backend/SSE change to cabinet runs.** Deleting the dialog and rendering in LOG is frontend-only; the `cabinet_run` snapshot (incl. Willie solver `children`) is unchanged.
- **No change to cabinet run *triggering*.** The `Run Cabinet` controls stay in CABINET/HomeOverview; full subsume only moves where the run *log* is displayed (modal → sidebar LOG card).
- **No test runner introduction.** The Dashboard package has no vitest/jest today; do **not** add one here. Keep `deriveLogEntries` pure so it is trivially unit-testable if a runner lands later; verify this slice live.
- **No persisted/wire/replay change**, so no compat code and no wipe-and-regen.

## Verification (live)

No dashboard unit-test runner exists, so verify in the running dashboard from the worktree (launch RimBob from the worktree on its own non-5000 port per launch-from-worktree practice, give the worktree dashboard URL):

1. Sidebar shows `COLONY | LOG`; COLONY is default and visually unchanged; switching to LOG and back works; LOG stays visible across scope changes (open a minister inspector — LOG persists).
2. With no events yet, LOG shows its empty state; COLONY empty/error states still render correctly under the COLONY tab (and do not blank the LOG tab).
3. Click **Apply** on an advice action → within one system-health poll an "Applied · …" card appears (ok tone); a failing apply shows a warn/error card with the backend `message` in its tooltip. Confirm against the live apply response + the SYSTEM Assisted Apply table (verify state via JSON/API, not the snapshot export).
4. Click **Run Cabinet Now** → the sidebar auto-switches to LOG and the active run's card auto-expands, showing live progress (cabinet_run SSE): the full step tree incl. nested Willie solver subitems (from the landed `cabinet-toast-solver-log`), with step status/duration updating live; a failed run shows error tone and the POST-failure step. After completion the card collapses to its summary; re-expanding any of the last ~12 run cards shows that run's full tree. Confirm **no floating modal appears** (dialog deleted) and the nested tree does not overflow the sidebar column.
5. Trigger a minister escalation (rules path requesting LLM) → a warn escalation card appears; confirming the LLM path flips it to an "LLM used" (ok) or "LLM failed" (error) card on the next health poll, matching the MinisterEscalationCallout.
6. Force a critical advice item → a warn "Critical · …" card appears once.
7. Cards are compact, titles wrap (no truncation), every emoji chip has a hover tooltip, and the card tooltip carries full detail.

## Dashboard reflection

This *is* a dashboard surface; it reflects existing backend state (apply attempts, cabinet runs, minister traces, active advice) with no new persisted artifact, so there is no hidden backend log to expose.

## Docs

Same-turn doc update (Documentation Discipline): [Docs/design/dashboard.md](Docs/design/dashboard.md) — update the **Right Sidebar** section and the **Information Architecture** "four stable regions" / right-sidebar line: the sidebar now carries a shallow `COLONY | LOG` tab bar; COLONY is the default and keeps the always-on colony context; LOG is a curated, capped, cross-scope feed of the most important discrete events (actions applied, cabinet runs, escalation/LLM outcomes, new critical advice) rendered as small icon-led cards with tooltips. Note the explicit role split vs SYSTEM Events (raw firehose) and ANALYTICS (aggregates). Record that COLONY remains the default tab so sidebar colony context is never hidden by default.

Also update the cabinet run-step **dialog** references (the Manual Triggers "run-step dialog" notes near [dashboard.md:508](Docs/design/dashboard.md) and the CABINET run-dialog / `cabinet_run` SSE description near [:573](Docs/design/dashboard.md)): the run-step log is no longer a modal opened on click — it renders as the LOG tab's expandable cabinet-run card, which auto-focuses (sidebar switches to LOG) and live-updates on a manual run, and keeps the last ~12 runs re-expandable. The `cabinet_run` SSE contract and the `run_id`-before-POST reconciliation are unchanged; only the surface that renders them moves from a floating dialog to the sidebar LOG card.

## No compat

No compat code; no wipe-and-regen. There is no persisted, wire, or replay shape change — this is a read-only frontend derivation over inputs the dashboard already consumes. Do not add any tolerant parser or legacy branch.

## Refactor notes (flagged, not required this slice)

- **Duplicated time formatting** — `formatTime` ([Timeline.tsx](Dashboard/src/components/shared/Timeline.tsx)), `formatTraceTime` (MinisterEscalationCallout), `formatLoadedAt`/`formatSnapshotCaptured` (ColonySidebar) are near-identical. A shared `formatClockTime`/`formatRelativeTime` helper would remove the copies; LOG will want one too. Worth a small follow-up rather than adding a fourth copy.
- **SYSTEM Events could consume `deriveLogEntries`** once it exists, collapsing two ad-hoc renderings into one source of truth. Noted as a future cleanup, not done here.

## Open decisions

- **Sidebar tab persistence** — this slice keeps the selected sidebar tab in ephemeral `useState` (resets to COLONY on reload). The dashboard already persists scope/view in browser storage; persisting the sidebar tab the same way is a trivial optional add. Recommend shipping ephemeral first.
- **Card count cap** — proposed ~40; tune during live verification for sidebar height.
- **Auto-switch aggressiveness** — this slice auto-switches the sidebar to LOG and auto-expands on every manual cabinet run. If that steals focus from COLONY too aggressively, the fallback is to switch only when the sidebar is already on LOG, else just badge the LOG tab. Ship the auto-switch first; tune live.
