# RimAI - State Store

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

The state store keeps the current colony snapshot in a single visible root. The
root contains domain aggregates for people, stockpiles, buildings, power, map
context, threats, research, factions, economy, and other live state as slices
need them.

Design rules:

- Aggregates hold what is true, not what it means.
- Cross-aggregate interpretation belongs in derived views/briefings.
- Aggregates should not call RIMAPI, LLMs, or ministers.
- Versioning should make cache invalidation explicit and debuggable.

The exact aggregate types and fields live in `Src/StateStore/` and
`Src/Common/Aggregates/`.

---

## View Caching

Briefings are cached derived views over aggregates. A cached view is valid only
while the aggregate versions it read remain unchanged. Recompute lazily when a
dependency changes.

This stays pull-based and inspectable. No reactive framework is required for the
current scale.

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

Food's briefing should answer the nutrition-chain questions:

- Is the current food buffer safe?
- Which part of the chain is limiting: acquisition, cooking, storage, freezer,
  labor, season, threat, or data coverage?
- What concrete opportunity is available now: harvest, wild plants, sowing,
  cooking, storage visibility, freezer/building request, or escalation?

The implemented Food briefing is narrower than the eventual target, and that is
fine. Use code/tests for current fields; use this doc for the design direction.

### Future Briefings

Future minister briefings should follow the same pattern:

- Defense: active threats, readiness, perimeter, weapons, casualty risk.
- Construction: build queue, materials, power, room program, structural risks.
- Welfare/Medical: mood, needs, recreation, schedules, health, hospital
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
- [ ] Whether briefings need explicit stale-after timestamps.
