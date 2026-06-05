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

## Files

- [Dashboard/src/dashboard/selectors.ts](Dashboard/src/dashboard/selectors.ts) — add `LogEntry`/`LogChip`/`LogEntryKind`/`LogTone` types + `deriveLogEntries(...)` pure selector.
- [Dashboard/src/components/layout/ColonySidebar.tsx](Dashboard/src/components/layout/ColonySidebar.tsx) — add the COLONY|LOG tab bar; restructure so empty/error states scope to the COLONY tab only; render the LOG tab.
- New `Dashboard/src/components/layout/SidebarLog.tsx` (or an in-file section) — the LOG card list + empty state.
- Dashboard CSS (the existing global stylesheet the sidebar uses) — `.sidebar-tabs*` and `.log-card*` rules, reusing existing tone tokens.
- [Dashboard/src/App.tsx](Dashboard/src/App.tsx) — pass `systemHealth.data`, `feed.cabinetRuns`, and critical advice into `ColonySidebar`.

## Out of scope / non-goals

- **No backend change.** No new endpoint, no new SSE event type, no change to `/api/system/health`, traces, or the apply contract. Pure frontend over existing inputs.
- **No new polling or hooks.** Reuse the existing `systemHealth` poll and the `cabinet_run` SSE feed. The 15s system-health cadence means applied/escalation cards can lag up to ~15s; acceptable for a log, and explicitly **not** worth adding a faster poll or threading an apply-callback event bus in this slice.
- **No connectivity / build-served cards** this slice (deferred candidates above).
- **No SYSTEM Events refactor** to share the selector (possible later cleanup, noted not done).
- **No test runner introduction.** The Dashboard package has no vitest/jest today; do **not** add one here. Keep `deriveLogEntries` pure so it is trivially unit-testable if a runner lands later; verify this slice live.
- **No persisted/wire/replay change**, so no compat code and no wipe-and-regen.

## Verification (live)

No dashboard unit-test runner exists, so verify in the running dashboard from the worktree (launch RimBob from the worktree on its own non-5000 port per launch-from-worktree practice, give the worktree dashboard URL):

1. Sidebar shows `COLONY | LOG`; COLONY is default and visually unchanged; switching to LOG and back works; LOG stays visible across scope changes (open a minister inspector — LOG persists).
2. With no events yet, LOG shows its empty state; COLONY empty/error states still render correctly under the COLONY tab (and do not blank the LOG tab).
3. Click **Apply** on an advice action → within one system-health poll an "Applied · …" card appears (ok tone); a failing apply shows a warn/error card with the backend `message` in its tooltip. Confirm against the live apply response + the SYSTEM Assisted Apply table (verify state via JSON/API, not the snapshot export).
4. Click **Run Cabinet Now** → a "Cabinet run …" card appears live (cabinet_run SSE), with step count + duration chips; a failed run shows error tone.
5. Trigger a minister escalation (rules path requesting LLM) → a warn escalation card appears; confirming the LLM path flips it to an "LLM used" (ok) or "LLM failed" (error) card on the next health poll, matching the MinisterEscalationCallout.
6. Force a critical advice item → a warn "Critical · …" card appears once.
7. Cards are compact, titles wrap (no truncation), every emoji chip has a hover tooltip, and the card tooltip carries full detail.

## Dashboard reflection

This *is* a dashboard surface; it reflects existing backend state (apply attempts, cabinet runs, minister traces, active advice) with no new persisted artifact, so there is no hidden backend log to expose.

## Docs

Same-turn doc update (Documentation Discipline): [Docs/design/dashboard.md](Docs/design/dashboard.md) — update the **Right Sidebar** section and the **Information Architecture** "four stable regions" / right-sidebar line: the sidebar now carries a shallow `COLONY | LOG` tab bar; COLONY is the default and keeps the always-on colony context; LOG is a curated, capped, cross-scope feed of the most important discrete events (actions applied, cabinet runs, escalation/LLM outcomes, new critical advice) rendered as small icon-led cards with tooltips. Note the explicit role split vs SYSTEM Events (raw firehose) and ANALYTICS (aggregates). Record that COLONY remains the default tab so sidebar colony context is never hidden by default.

## No compat

No compat code; no wipe-and-regen. There is no persisted, wire, or replay shape change — this is a read-only frontend derivation over inputs the dashboard already consumes. Do not add any tolerant parser or legacy branch.

## Refactor notes (flagged, not required this slice)

- **Duplicated time formatting** — `formatTime` ([Timeline.tsx](Dashboard/src/components/shared/Timeline.tsx)), `formatTraceTime` (MinisterEscalationCallout), `formatLoadedAt`/`formatSnapshotCaptured` (ColonySidebar) are near-identical. A shared `formatClockTime`/`formatRelativeTime` helper would remove the copies; LOG will want one too. Worth a small follow-up rather than adding a fourth copy.
- **SYSTEM Events could consume `deriveLogEntries`** once it exists, collapsing two ad-hoc renderings into one source of truth. Noted as a future cleanup, not done here.

## Open decisions

- **Sidebar tab persistence** — this slice keeps the selected sidebar tab in ephemeral `useState` (resets to COLONY on reload). The dashboard already persists scope/view in browser storage; persisting the sidebar tab the same way is a trivial optional add. Recommend shipping ephemeral first.
- **Card count cap** — proposed ~40; tune during live verification for sidebar height.
