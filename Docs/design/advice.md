# RimBob - Advice: Schema, Lifecycle, Feedback

> **Living document.** See `AGENTS.md` for update rules.
> This doc records advice semantics and lifecycle decisions. Exact C# records,
> JSON serialization details, parser tolerance, and test fixtures live in source.

---

## Why This Doc Exists

Under the assisted-gameplay pivot, every minister's player-facing output is an
`AdviceItem`, and the player's reaction is the primary training signal.

This doc defines:

1. What advice means.
2. The Accept / Dismiss / Pushback lifecycle.
3. Minister-owned pushback lists.
4. How pushbacks feed refinement.
5. Player-confirmed Assisted Apply.
6. The future autonomy dial.

Feedback was moved to M5, just before M6 consumes it. `Modify` was replaced by
`Pushback`: the player tells the minister why it is wrong in natural language
instead of editing action text. Implicit state-diff feedback is not
part of MVP.

---

## AdviceItem Contract

`AdviceItem` is the stable player-facing unit. The code owns exact fields and
serialization; the design-level contract is:

- Identity and source: id, issuing minister, advice type.
- Urgency: one `priority` enum.
- Player content: title, body, rationale.
- Player/future-execution operations: concrete `actions[]`.
- Evidence: guide citations and briefing reference when available.
- Lifecycle metadata: issue time, expiry, supersession, autonomy mode at issue.

Exact field names live in `Src/Common/Advice/` and dashboard type mirrors.

### Priority

`priority` is the single urgency field on advice: `low`, `medium`, `high`, or
`critical`. It drives player display, active-advice sorting, tactical alert
behavior, and future arbitration. Do not reintroduce separate severity and score
knobs for advice.

Flags still use severity because flags are inter-minister routing signals, not
player-facing advice.

### Advice Type

`advice_type` is closed per minister. It is also the unit a future autonomy dial
graduates one at a time. Adding an advice type is a design decision for that
minister.

### Actions

`actions[]` are the single player-facing action list on an advice item. They
are concrete "do X" operations that may be prioritized by the emitter or UI but
do not imply one ordered procedure. Resource prerequisites that matter to the
player should appear as actions only when they are part of the actionable advice
surface.

Action kinds are shared across ministers and should describe the operation well
enough to stand alone in the dashboard and future Auto mapping. Prefer explicit
names such as `mark_harvest`, `mark_hunt`, `place_blueprint`,
`production_bill`, `set_priority`, `designate_zone`, `set_stockpile_zone`, and
`unforbid` over generic `note` output when the operation is known.

Each action carries one short imperative instruction. Optional quantity, owner,
work type, skill, reason, and icon metadata should be used only when the emitter
can state them cleanly. Explanation belongs in body/rationale/current-state
summary; action instructions should scan like compact UI/action primitives.

Actions may include an optional explicit `icon` ref when the emitter knows the
game def or id. Icon refs are rendering hints only; they are not execution
inputs and should not be inferred from prose.

Actions may also carry an explicit executable handle when the backend can map
that action to an Assisted Apply operation. The handle is produced by
deterministic code or accepted from model output only after strict validation;
prose instructions alone are never executable.

Labor-like actions must name a RimWorld work-tab type when possible. "Labor
capacity" by itself is too vague for advice, logs, or future Auto wiring.

### Resource Requests

`ResourceRequest` remains the cross-minister request shape on flags. It is no
longer a separate player-facing `AdviceItem` path.

Flag requests state what a minister needs from another subsystem to resolve an
issue: tiles, work-type-qualified labor, items, buildings, bills, stockpile
space, attention, or trade capacity. They are advisory in MVP and do not
allocate pawns, reserve tiles, create bills, or write to RIMAPI.

Tolerant parsing may still accept legacy advice payloads with `steps[]`,
`resource_requests[]`, and `suggested_actions[]`, but new prompts/specs should
emit `actions[]` directly.

### Briefing Reference

Advice should point back to the briefing snapshot or recoverable source data it
was based on. The dashboard uses this for inspection; refinement uses it for
replay.

### Expiry, Freshness, And Supersession

Advice freshness is primarily game-time based. Wall-clock issue times remain
audit metadata, but gameplay advice should expire against the latest known game
tick or by resolved game state, not because RimWorld was paused, closed, or
RIMAPI was unreachable while real time passed. Stale cards are harmful when
treated as current, especially for threat or emergency advice, but they are
still useful inspection evidence.

Expired advice should not disappear from the latest minister snapshot. The
dashboard should keep showing the last persisted advice and clearly mark it as
expired/stale. Assisted Apply may only execute after fresh live validation and
must reject expired game-tick advice.

When new advice replaces an older unresolved item, use supersession or a stable
same-issue id so the active dashboard view updates instead of stacking duplicate
cards. History belongs in the Host-resolved logs root's replay corpus, not in
active cards.

### Active Advice Snapshots

The advice surface is the issuing minister's latest successful view, not an
append-only feed and not a wall-clock TTL cache. Each minister play cycle should
publish its current set as a minister-scoped snapshot. A successful empty
snapshot means the minister currently has no advice items.

Active feeder snapshots are durable across Host restarts through the unified
minister output store. The dashboard should reload the last good advice,
state-summary, and chain snapshot immediately after boot instead of blanking
until the next cabinet cycle. This store is latest-only; historical advice
records remain in the replay corpus.

Feeder snapshots may carry one player-facing current-state summary above the
advice items. That summary describes the whole minister read; it is not an
`AdviceItem`, a resource request, or an executable action. For Food, this
summary is a compact briefing-derived bullet list so it stays factual about
stores, growing areas, acquisition, storage/kitchen state, and data gaps even
when the LLM writes its own raw `state_summary`.

Feeder snapshots may also carry a deterministic chain model for the whole
minister read. This is not an `AdviceItem` field and is not authored by the
LLM. For Food, backend code derives the model from `FoodBriefing` plus the
emitted active advice so the dashboard can show how the current food pressure
connects to grow, hunt, forage, cook, storage, and meal outcomes.

If a cycle fails before producing rules output or successful LLM output, keep
the previous active snapshot rather than clearing it.

### Autonomy At Issue

Advice records the autonomy mode in effect when it was emitted. MVP advice is
always `Suggest`; the field exists so future Auto-mode advice can be separated
in logs and UI. Assisted Apply is still `Suggest` advice because the player click
is the consent boundary.

---

## Lifecycle

Advice starts active, then transitions to one archived state:

- Accepted: the player read it and intends to act.
- Dismissed: the player read it and will not act.
- Pushback: the player explains why the minister was wrong.
- Superseded: newer advice replaces it.
- Expired: no explicit feedback arrived before the card aged out or resolved.

State transitions emit feedback events. Pushback additionally appends to the
issuing minister's pushback list.

Assisted Apply may add an `Applied` or apply-result record for the specific step
that was executed. That record is operational evidence, not implicit praise of
the advice unless the player also Accepts it.

---

## Feedback Events

Feedback events record the advice id, issuing minister, action, optional note or
pushback text, and timing context. Exact event shape lives in code.

`pushback_text` is only present for Pushback. Short notes are for Accept/Dismiss
only.

---

## Explicit Feedback

Feedback buttons are:

- **Accept** - "I read this and will act on it."
- **Dismiss** - "I read this and am not acting on it."
- **Pushback** - "You are wrong, and here is why."

Pushback is the highest-value signal. It captures the player's correction in
their own words, which refinement can cluster into rule, prompt, briefing, or
RAG improvements.

---

## Minister-Owned Pushback Lists

Each minister owns and persists its own pushback list. Pushbacks are scoped: the
Mayor does not read Food's pushbacks, and Food does not read Defense's.

Design requirements:

- Durable across sessions.
- Append-only by default.
- Includes enough context to understand what advice was corrected.
- Readable by the issuing minister for later prompt context and refinement.

The exact storage path and JSONL shape are implementation details; check source
and tests before editing.

Pushbacks have two roles:

- **Inline correction (M5):** recent relevant pushbacks can be injected into the
  issuing minister's prompt as player corrections.
- **Refinement input (M6):** the full list is clustered to propose durable
  changes.

If a correction is relevant to multiple ministers, the player can push back on
each minister's advice separately. Cross-minister de-duplication is not MVP.

---

## Implicit Feedback

Implicit state-diff feedback was dropped from MVP. The explicit Pushback channel
is load-bearing, and the earlier implicit mapping from action kinds to state
fields was speculative.

If a real gap appears after feedback is wired, add implicit feedback later with
evidence from actual play.

---

## How Feedback Feeds Refinement

Refinement clusters one minister's pushbacks and replay records. Repeated
corrections can become candidate rule changes, prompt edits, briefing fields, or
RAG retrieval changes.

Examples:

- Repeated winter hunt pushbacks can promote a rule suppressing unsafe hunt
  advice when there are no viable targets.
- Repeated "we already have a freezer" pushbacks can promote a briefing field or
  prompt constraint.
- Long accepted streaks reinforce that a rule is working.

Approval gates and replay requirements live in [`evaluation.md`](evaluation.md).

---

## Assisted Apply

Assisted Apply is the MVP path for manually executing the safest concrete advice
actions. It is not an autonomy mode and does not let a minister act on its own.

An action is eligible only when all of these are true:

- The action kind maps to an allowlisted, single-operation RIMAPI write.
- The target is explicit and can be revalidated against fresh state.
- The operation does not allocate pawns, force jobs, change schedules, edit
  arbitrary bills, create broad zones, or make combat/medical/prisoner decisions.
- The Host can read back or otherwise observe the expected result.
- The dashboard requires an explicit player click for that one action.

The LLM never chooses raw endpoints, payloads, or arbitrary target ids. It may
emit a structured action; deterministic code decides whether that action can expose
Apply. Initial candidates should be conservative: `unforbid` for known item
stacks, `mark_harvest` for validated safe plant clusters, `mark_hunt` for
deterministic low-risk animal batches, and one Food-owned simple-meal cook-bill
upsert. The bill operation is intentionally narrow:
exactly one current cooking workbench, backend-resolved simple-meal recipe,
bounded do-until target, idempotent add-or-update, no delete/reorder/suspend
changes, fresh-state revalidation, and RIMAPI read-back. `mark_hunt` is eligible
only when Food has already excluded risky/tame/unhealthy animals, has exact
animal ids and a bounded rectangle, and fresh validation proves no unsafe or
off-target animals are inside that rectangle. Work priorities, broad bill
editing, zones, pawn assignment, equipment, medical, prisoner, and combat
controls stay outside the first Assisted Apply slice.

Apply attempts must be logged with enough context to inspect the advice, target,
validation decision, RIMAPI result, and read-back state in dashboard/system
surfaces.

When an apply returns `applied` or `already_satisfied`, the active advice
snapshot should stop offering that same executable action. Future minister
refreshes should also suppress the action when fresh state proves the operation
is already satisfied.

---

## Autonomy Dial

The future autonomy dial is per minister and advice type:

- **Off:** do not emit this advice type.
- **Suggest:** emit advice; the player decides.
- **Auto:** emit advice and execute through the re-engaged Auto stack.

MVP honors only `Suggest`. Assisted Apply is a manual click path attached to
`Suggest` advice. `Auto` returns in M7+ after planner, Labor, broad write
coverage, and per-minister trust gates exist.

---

## Open Questions

- [ ] How should prompt-time pushbacks be selected: recency, relevance, age, or
      token budget?
- [ ] Do pushbacks ever expire, or are stale corrections handled by refinement?
- [ ] How should tactical advice conflicts be surfaced before a full CoS loop
      exists?
- [ ] Define exact per-minister advice type catalogues in the relevant minister
      docs.
- [ ] Calibrate step-kind granularity as feeders and future Auto mapping
      mature.
