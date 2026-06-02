# Willie Build Queue — fix the confusing "Proposed" empty-state copy

> **Frontend copy + small conditional-logic slice. One file.** Make the Willie
> **Build Queue → Proposed** empty-state explain the *real* upstream reason the
> lane is empty (no build has been requested of Willie yet — e.g. Chef hasn't
> run) instead of the jargon string *"Solver placement options appear here when
> Willie emits `options[]`."*

---

## 0. The complaint

RIMAPI is alive, the dashboard loads, but **Build Queue → Proposed** shows:

> *"Solver placement options appear here when Willie emits `options[]`."*

Two problems the operator named:

1. **It's confusing.** It reads like an internal-error / contract note. `options[]`
   is implementation jargon; it tells the operator *what* is missing, not *why*,
   and gives no next action. (It's an empty-*state* hint, not an error — but the
   wording invites the "is something broken?" reading.)
2. **It hides the real cause.** In the current run the Proposed lane is empty
   because **Chef hasn't run yet, so no build request has reached Willie** — there
   is nothing for the placement solver to place. The copy never says this.

---

## 1. Root cause (why Proposed is empty)

The Proposed lane renders `AdviceItem.options[]` for the Willie scope
([`buildProposedOptions`, `MinisterBuildQueueView.tsx:356`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)).
Options only attach at the **end** of this chain
([`MinisterOfWillie.RunPlayCycle`](../Src/Ministers/Willie/MinisterOfWillie.cs)):

```
another minister (e.g. Chef) emits a building_request  →  shows in REQUESTED lane
        ↓                       (requested_from: "Willie")
Willie selects a placement trace  (Rules.cs)
   • building_request_active      ← needs an inbound request   ← THIS link is the gap today
   • kitchen/hospital/storage_missing  ← Willie self-triggers from missing-room evidence
        ↓
TrySolvePlacementAsync → IPlacementSolver.SolveAsync
        ↓
solver returns ≥1 validated option  (not no-fit)
        ↓
EnrichAdviceItem attaches options[]  →  shows in PROPOSED lane
```

So the lane is empty whenever **any** link upstream hasn't produced options. The
operator's case is the **first** link: no `building_request` aimed at Willie has
been emitted (Chef hasn't run), so `building_request_active` never fires and the
solver is never driven for a requested build. The dashboard already exposes this
exact signal — it's the **Requested** lane right above Proposed
([`buildRequestCards`, `MinisterBuildQueueView.tsx:344`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)),
which is *also* empty in this run.

> Note: Willie can *self*-trigger a placement via the missing-room rules without
> any inbound request, so "no request" is not the **only** way Proposed stays
> empty — but it is the operator's case and the dominant freezer/Chef flow. The
> new copy stays truthful for both (see §2).

This plan is **copy + a `requests.length` branch only**. *Why* options fail to
attach when the solver *is* driven (no-fit diagnosis: missing anchor, no path,
validation reject) is already owned by the verification runbook
[`rimapi-room-reads-live-verify.md`](rimapi-room-reads-live-verify.md) and the
driving advice **Rationale** (`"Placement solver no-fit: …"`). Out of scope here.

---

## 2. Design — context-aware Proposed empty-state

Replace the single static `LaneEmpty` at
[`MinisterBuildQueueView.tsx:73`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)
with a message chosen from the signal the lane **already has in scope**:
`requests.length` (the Requested lane count, computed at line 39). No new data,
no new prop, no backend call.

**Case A — `requests.length === 0`** (nothing has asked Willie to build — the
operator's case):

> *"No build has been requested of Willie yet, so the placement solver has
> nothing to place. When another minister — e.g. Chef — requests a build it
> appears under **Requested** above, and Willie's validated placement options
> show up here. Willie can also self-propose when it detects a missing room."*

**Case B — `requests.length > 0`** (a request exists but no options attached —
the solver ran and returned no validated footprint, *or* hasn't solved yet):

> *"Willie has an open build request but the solver hasn't returned a validated
> footprint yet. Open the driving advice on the **Advice** tab for the reason
> (missing anchor, no reachable path, or failed validation)."*

Both drop the `options[]` jargon, name **Chef** + the **Requested** lane, and give
a next action. Case A is the precise answer to the operator's report.

### Implementation sketch (single file)

```tsx
// near the other lane builders at the top of MinisterBuildQueueView
function proposedEmptyHint(requestCount: number): string {
  return requestCount === 0
    ? 'No build has been requested of Willie yet, so the placement solver has nothing to place. ' +
      'When another minister — e.g. Chef — requests a build it appears under Requested above, ' +
      'and Willie’s validated placement options show up here. Willie can also self-propose when it detects a missing room.'
    : 'Willie has an open build request but the solver hasn’t returned a validated footprint yet. ' +
      'Open the driving advice on the Advice tab for the reason (missing anchor, no reachable path, or failed validation).';
}
```

```tsx
// line 71–79, Proposed section
<BuildQueueSection title="Proposed" count={proposedOptions.length} iconKey="place_blueprint">
  {proposedOptions.length === 0 ? (
    <LaneEmpty>{proposedEmptyHint(requests.length)}</LaneEmpty>
  ) : (
    <div className="build-option-grid">
      {proposedOptions.map(card => <OptionCard card={card} key={`${card.item.id}:${card.option.id}`} />)}
    </div>
  )}
</BuildQueueSection>
```

`requests` is already in render scope (line 39), so the change is local to the
Proposed branch.

---

## 3. Optional polish (note, not required — keep scope tight)

The operator only flagged **Proposed**. Two symmetric copy nits may be folded in
if cheap, otherwise leave them:

- **Requested empty copy** ([line 63](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)):
  *"Building requests aimed at Willie will appear here."* → could name the source:
  *"Other ministers — e.g. Chef — post build requests for Willie here. None are
  open right now."* Reinforces the same mental model as Case A.
- The whole-view `EmptyState` at [line 57](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)
  already reads well; leave it.

**Explicit non-goals** (separate slices if ever wanted):
- No structured "solver ran / no-fit" marker on `AdviceItem` — Case B can't yet
  distinguish *solver-ran-no-fit* from *never-solved* without parsing the prose
  Rationale, which is fragile. A structured flag is a backend change, out of scope.
- No solver / RIMAPI / `options[]` production change — that's
  [`rimapi-room-reads-live-verify.md`](rimapi-room-reads-live-verify.md).

---

## 4. Files

| File | Change |
|---|---|
| [`Dashboard/src/components/minister/MinisterBuildQueueView.tsx`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx) | Add `proposedEmptyHint(requestCount)`; swap the static string at line 73 for `proposedEmptyHint(requests.length)`. *(Optional §3: reword line 63.)* |

Single file. No types, no API, no test infra (Dashboard has no Vitest runner).

---

## 5. Verify

- `npm run build` in `Dashboard/` (runs `tsc && vite build`) — typecheck + bundle
  clean. There is no frontend unit-test runner, so this is the mechanical gate.
- **Visual / live** (the operator's repro): with RIMAPI alive but Chef not yet
  run, open **Willie → Build Queue → Proposed** and confirm it now reads the
  Case-A message (names Chef + Requested), not the `options[]` string. After a
  Chef build request lands but before the solver fits, confirm Case-B copy.
- Snapshot capture optional; this is a copy change, JSON state is unaffected.

---

## Scope boundary

Frontend empty-state **copy + one `requests.length` branch** in a single
component. No backend, no solver, no schema, no new dependency. Does **not** try
to make options attach — only to explain, in plain language with a next action,
why the lane is empty when they don't.
