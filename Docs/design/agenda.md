# RimBob - The Mayor's Agenda

> **Living document.** See `AGENTS.md` for update rules.
> This doc records Agenda semantics and durable contracts. Exact JSON records,
> SSE route shapes, dashboard components, and feedback payload fields live in
> source and tests.

---

## What The Agenda Is

The Agenda is the structured player-facing snapshot the Mayor produces. It is
persisted through the unified minister output store alongside feeder advice
snapshots.

In MVP, the Agenda is the Mayor's advice. There is no separate `daily_digest`
`AdviceItem`. The player reads it as a ranked to-do list: free-text bullets for
short-term priorities and long-term goals, plus a short explanation of what
changed.

The Agenda is also the read-only interface between the Mayor and feeder
ministers. Feeders read the latest persisted Mayor snapshot for
`cabinet_direction`; this remains a sanctioned Mayor-to-feeder channel without
direct minister-to-minister messaging.

---

## Shape

Design-level fields:

- Version and timing metadata.
- Strategic posture.
- `state_of_the_union`: one terse interpretive sentence per relevant category.
- `update_notes`: short state-change commentary for the current snapshot.
- `short_term`: free-text bullets with stable ids and status.
- `long_term`: free-text bullets with stable ids and status.
- `cabinet_direction`: optional free-text direction per active minister.
- `guide_citations`: server-stamped citations from RAG retrieval.

Exact record fields and serialization live in `Src/Common/Advice/`.

### Bullet Structure

Agenda bullets are free text. Do not add structured action-kind catalogues,
domain tags, deadline fields, or nested schemas unless implementation proves
they are needed.

Bullet ids should remain stable for obvious recurring issues when the Mayor can
infer them from the current briefing and flags. The server no longer feeds the
prior Agenda back into the Mayor prompt, so ids are no longer a server-side
continuity contract.

Statuses are `active`, `completed`, and `deferred`. Completed/deferred items may
stay visible briefly so the player sees the change, then move to history.

### State Of The Union

`state_of_the_union` is interpretation, not raw telemetry. Raw numbers belong in
the sidebar/briefing views. The Mayor should emit short category sentences such
as food, defense, welfare, construction, treasury, and research only when worth
surfacing.

### Cabinet Direction

`cabinet_direction` is plain-English direction for active ministers. It is not
parsed as a command and does not fire a wake event. Ministers may use it in
prompt context or rules ranking the next time they evaluate.

The Mayor is the only writer of the Agenda. Ministers never mutate it.

### Guide Citations

Guide citations are server-stamped from retrieved guide context so the dashboard
can render evidence even if the LLM omits or misuses citation ids. Empty
citations are valid when RAG is disabled, unavailable, or irrelevant.

---

## Turn-End Update

At agenda time, the Mayor receives the latest briefing, active relevant flags,
trends, and guide context. It does not receive the previous Agenda. The Mayor
outputs a complete new Agenda, not a patch. The unified minister output store
owns version stamping, latest-snapshot persistence, and reload on Host startup.

The latest Mayor Agenda is durable Host runtime state under the stable
machine-local data root, stored as `ministers/mayor.json` below
`RimBob:DataRoot` or the default LocalAppData RimBob root. The store is
latest-only; history belongs in the replay corpus.

If Host starts with no persisted minister output at all, it initializes a
conservative server-side bootstrap Agenda from the current briefing before the
dashboard is served. The bootstrap must be clearly labeled in `update_notes`,
should only contain safe `Suggest`-mode priorities, and is replaced by the next
successful Mayor LLM run. A rebuild with any persisted minister output reloads
that output and does not fire the cabinet just to populate the dashboard. If the
Mayor LLM fails twice before any Agenda exists, the Mayor uses the same
bootstrap path rather than leaving the Mayor snapshot empty.

Manual dashboard runs refresh live state and evaluate RimBob through the cabinet
trigger path. They are RimBob evaluation controls, not game writes.

---

## Relationship To AdviceItem

| Concept | What it is | Producer | Timing |
|---|---|---|---|
| `MayorAgenda` | Structured Mayor snapshot | Mayor | Bootstrap only on empty first run, then daily/manual refresh |
| `AdviceItem` | Tactical/operational advice | Feeder ministers | Minister triggers |
| Tactical alert | Urgent interruption | Future Mayor/feeder path | Severity spike |

In M1, only Agenda existed. From M3 onward, feeder `AdviceItem`s coexist with
the Agenda.

---

## Feedback Model

Feedback attaches to individual short-term or long-term bullets, not to the
Agenda document as a whole.

The feedback actions are:

- **Accept** - "I will act on this."
- **Dismiss** - "I am not doing this."
- **Pushback** - "This is wrong, and here is why."

There is no implicit feedback for Agenda items in MVP. Feedback is explicit only. Pushback text is the highest-value refinement signal and should feed the Mayor's Oracle-led refinement path.

Exact feedback payload shape lives with the advice/feedback code contract.

---

## Dashboard Contract

The Agenda is the Mayor's primary player-facing view. The dashboard should show:

- Posture and last updated time.
- State of the Union category sentences.
- Update notes.
- Short-term bullets with delta/status indicators.
- Long-term bullets.
- Guide citations where available.
- Feedback controls only when the feedback lifecycle is actively wired.

Dashboard layout and component details live in [`dashboard.md`](dashboard.md)
and Dashboard source.

---

## API / SSE Contract

The design contract is:

- The dashboard can fetch the latest Mayor snapshot through the minister
  snapshot route.
- The advice stream can broadcast full agenda updates.
- New dashboard connections receive the current agenda when one exists.
- The latest Mayor snapshot endpoint returns the persisted current agenda after
  Host restart. During normal Host startup, missing output state is initialized
  with a labeled bootstrap Agenda only on a genuine empty first run; `204`
  should only appear if initialization cannot complete or output storage is
  intentionally unavailable.
- Idle streams stay alive.
- Manual refresh triggers the same safe `Suggest`-mode cabinet evaluation path.

Exact endpoint names, event framing, and payload fields live in Host endpoint
code and Dashboard API clients.

---

## Open Questions

- [ ] Should short-term bullets be capped to force ranking?
- [ ] Should the player manually mark a bullet complete mid-day, or only the
      Mayor on the next update?
- [ ] Should long-term bullets support feedback, or remain informational?
- [ ] If rules eventually need to parse `cabinet_direction`, what is the
      smallest useful structure?
