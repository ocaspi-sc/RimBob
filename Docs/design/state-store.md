# RimBob - State Store

> **Living document.** See `AGENTS.md` for update rules.
> This doc defines state-store responsibilities and briefing principles. Exact
> aggregate classes, briefing fields, DTO mappings, and cache helpers live in
> source and tests.

---

## Purpose

Ministers never call RIMAPI directly. They read from the state store: a curated
layer that absorbs RIMAPI data on appropriate cadences and computes the derived
facts ministers need.

```text
Ingestion -> domain aggregates -> derived views / briefings
RIMAPI       what is              what it means
```

---

## Domain Aggregates

The state store keeps the current colony snapshot in a single visible root. The root contains domain aggregates for people, stockpiles, map areas, stored items, item defs, animal defs, buildings, work-table bill state, power, map context, threats, research, factions, economy, Willie construction backlog, and other live state as slices need them.

Design rules:

- Aggregates hold what is true, not what it means.
- Cross-aggregate interpretation belongs in derived views/briefings.
- Aggregates should not call RIMAPI, LLMs, or ministers.
- Versioning should make cache invalidation explicit and debuggable.

The exact aggregate types and fields live in `Src/StateStore/` and
`Src/Common/Aggregates/`.

---

## Normalized Game Time

RIMAPI exposes `game_tick` plus a human-readable date string such as
`5th of Aprimay, 5500, 14h`. RimBob should normalize all game dates around
elapsed colony time from game start, using `game_tick` as the numeric source of
truth and preserving the raw RIMAPI date only as source evidence.

The normalized game-time model should distinguish:

- `total_days`: fractional elapsed days since game start.
- `completed_days`: integer elapsed full days since game start.
- `colony_day`: one-based absolute colony day.
- `colony_year`: one-based colony year, using RimWorld's 60-day years.
- `day_of_year`: one-based day inside the current colony year.
- `quadrum`, `quadrum_day`, and `hour`: parsed calendar display fields when
  RIMAPI's date string is parseable.
- `rimworld_year`: the raw RIMAPI calendar year, retained for diagnostics.

Player-facing labels, briefing fields, advice freshness text, replay records,
and dashboard colony context should read from this normalized model. The
RIMAPI calendar year (`5500`, `5501`, etc.) should not appear as the main
player-facing year. Wall-clock operational timestamps such as `generated_at`,
`captured_at`, log times, and build datetimes remain UTC timestamps and are not
normalized to game time.

This is a wire/persistence schema boundary. When the code slice lands, bump the
affected persisted schemas and use no compat code; wipe-and-regen on upgrade.

---

## View Caching

Briefings are cached derived views over aggregates. A cached view is valid only
while the aggregate versions it read remain unchanged. Recompute lazily when a
dependency changes.

This stays pull-based and inspectable. No reactive framework is required for the
current scale.

## Snapshot Persistence

Host persists the latest full curated `ColonyState` snapshot under the stable
machine-local data root after a complete live RIMAPI refresh succeeds. This is
not raw RIMAPI endpoint mirroring; it is the aggregate root ministers already
read. The default location is `state/latest-colony-state.json` below
LocalAppData's RimBob data root, overrideable through `RimBob:DataRoot`.

On startup, Host may restore that latest snapshot before agenda bootstrap so
the dashboard and bootstrap briefings can show last-known colony state when
RIMAPI is down. Restored state is explicitly marked snapshot-sourced and stale
until the first successful live refresh replaces it. Runtime health and the
dashboard must surface that origin instead of treating restored aggregates as
live.

The snapshot is latest-only for now. Aggregate version counters restart after a
restore because versions are process-local cache invalidation signals, not
durable history. Timestamped state history and per-aggregate staleness remain
future work. Assisted Apply still forces a fresh live refresh and must not
execute from restored snapshot state.

Snapshot schema changes do not carry compatibility readers. When the schema
version changes, Host deletes the stale latest snapshot and regenerates it from
the next successful live RIMAPI refresh.

SYSTEM must expose the resolved snapshot path and current snapshot metadata so
operators can distinguish real live state from restored stale state and verify
the snapshot is not tied to the active checkout or worktree.

## Minister Output Persistence

Host also persists each minister's latest player-facing output under the stable
machine-local data root. The unified minister output store writes one file per
minister under `ministers/<minister>.json`: the Mayor stores a typed
`MayorAgenda`, and feeder ministers store their typed `AdviceSnapshot`
including advice items, state summary, and chain data.

This store is not a history system and does not replace the replay corpus. It
exists so the dashboard and SSE replay can show the last good output
immediately after Host restart without running a cabinet cycle merely because
the executable was rebuilt. SYSTEM should expose the output root plus
per-minister persisted time, generation, and load/flush state.

---

## Refresh Cadence

Ingestion cadence is independent from minister LLM cadence. Pollers keep
aggregates fresh; ministers wake when relevant briefing versions change or when
an explicit trigger fires.

Target cadence tiers:

| Tier | Examples |
|---|---|
| Static | Terrain grid, defs, world map structure |
| Slow | Faction relations, world settlements, completed research |
| Medium | Stockpiles, buildings, power grid, weather, mood |
| Fast | Pawn positions, current jobs, active incidents, energy grid |
| Event-diff | Letters, alerts, incident list compared against prior snapshot |

RIMAPI's SSE endpoints stream camera video, not game events, so game-event
detection is polling/diff based.

Current implementation may batch-refresh needed endpoints before all cadence
tiers exist. The design requirement is that cadence becomes finer only when a
consumer needs it.

Food consumes terrain as compact fertility context for crop selection. The state
store keeps this as a derived summary from RIMAPI terrain/definition inputs; it
does not expose raw tile dumps to minister prompts.

### Event-Diff Signals

Signals worth detecting include raids/quests/events, alerts, combat log entries,
meaningful pawn job changes, construction completions, designations, social
events, and periodic resource/energy/research/thought snapshots.

Noise filter: routine pawn jobs such as haul, clean, and wander should not wake
ministers by themselves.

---

## Briefings

Each minister gets exactly one briefing object: its complete reasoning view for
that cycle. Briefings should stay tight and focused. Derived facts belong here
so prompts do not ask the LLM to do arithmetic or infer basic game state.

Briefing design rules:

- Prefer actionable summaries over raw dumps.
- Include uncertainty and coverage gaps when source data is missing.
- Keep spatial data aggregated: nearest clusters, proximity buckets, tile
  counts, distances, and bottleneck signals.
- Do not serialize raw plant, animal, building, pawn, or tile lists unless a
  specific rule proves it needs that detail.
- Exact field lists live in the briefing records and derivation tests.

### Food Briefing

Chef's briefing should answer the nutrition-chain questions:

- Is the current food buffer safe?
- Which part of the chain is limiting: acquisition, cooking, storage, freezer,
  labor, season, threat, or data coverage?
- What concrete opportunity is available now: harvest, forage plants, low-risk
  hunt targets with compact risk/value reasons, sowing, cooking, storage
  visibility, freezer/building request, or escalation?

Food should classify stored food from item stacks plus definition metadata when
that data is available. Summary rollups are still useful, but they do not win
over item-level meal/raw-food counts because RIMAPI can under-classify stored
food in `resources/summary`.

The implemented Food briefing is narrower than the eventual target, and that is
fine. Use code/tests for current fields; use this doc for the design direction.

### Willie Briefing

Willie has a first `IBriefing` implementation backed by state-store derivation.
It feeds the rules-only `MinisterOfWillie` slice: the briefing record,
`WillieBacklog` aggregate from `/api/v1/map/construction/backlog`, and room
anchor inventory from room/building state are now visible through the Willie
dashboard scope.

Current derived surfaces:

- Pending blueprint/frame groups, including blocked/disallowed counts and
  missing material totals.
- Room anchors keyed by `RoomClass` for future room-program and placement work.
- Home-area buildable-region anchors as fallback-only placement loci when no room anchor resolves.
- Willie data-coverage flags so rules can distinguish missing evidence from
  healthy state.

`WillieBacklog` and `MapAreaRegistry` are latest-state only and participate in the persisted `ColonyState` snapshot. Snapshot schema changes for these aggregates use no compat code; wipe-and-regen on upgrade.

### Future Briefings

Future minister briefings should follow the same pattern:

- Defense: active threats, readiness, perimeter, weapons, casualty risk.
- Willie: build queue, materials, power, room program, structural risks.
- Welfare: Mood & Needs signals, including mood, needs, thoughts, recreation,
  schedules, comfort, beauty, room pressure, and relationship pressure.
- Medical: health, wounds, disease, surgery, medicine stock, hospital
  readiness.
- Research: queue, dependencies, unlocks, posture fit.
- Economy: wealth pressure, trade goods, deficits, caravan opportunities.
- Labor: deferred Auto-epic assignment context.

---

## History And Trends

Some advice needs trajectories, not only current values. The state store should
retain bounded trend history for scalar metrics such as food buffer, mood,
wealth, threat pressure, power margin, and research progress.

Retention and sampling should be calibrated from actual minister needs. Do not
add broad history storage before a rule or briefing consumer exists.

---

## Derived Facts

Derived facts such as "days of food remaining", "defensive readiness", and
"wealth velocity" live in views, not raw aggregates. This keeps the domain state
clean and makes derivations independently testable.

---

## Open Questions

- [ ] Thread-safety requirements for the shared colony snapshot.
- [ ] Multi-map support: MVP assumes one home map.
- [ ] Trend-history retention and sampling once more ministers consume it.
- [ ] Whether briefings need explicit per-aggregate stale-after timestamps.
