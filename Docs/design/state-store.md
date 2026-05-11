# RimAI — State Store

> **Living document.** See `CLAUDE.md` for update rules.

---

## Purpose

Ministers never call RIMAPI directly. They read from the state store — a curated layer that absorbs RIMAPI data on appropriate cadences and computes derived facts ministers need.

Three layers:

```
Ingestion  →  Domain Aggregates  →  Views / Briefings
(RIMAPI)       (what is)            (what it means)
```

---

## Domain aggregates

All live in `ColonyState` — the root container.

```
ColonyState
├── Pawns           ColonistRegistry
├── Stockpiles      StockpileLedger
├── Buildings       BuildingRegistry
├── Power           PowerNetwork
├── Map             MapInfo
├── Threats         ThreatBoard
├── Research        ResearchState
├── Factions        FactionRegistry
├── World           WorldMap
├── Economy         EconomyLedger
└── Schedule        ColonySchedule
```

Each aggregate carries a **monotonic version number** that increments on any update. Nothing else; no reactive events. Consumers pull and check version.

`ColonyState` is the single root — all ingestion pollers write into it, all minister briefings read from it. No aggregate holds a reference to another aggregate; no cross-aggregate derivations happen here (those are views). This central-object pattern keeps state visible and debuggable: one `ColonyState.Dump()` gives a full colony snapshot.

---

## Versioning + view caching

```csharp
public class Versioned<T>
{
    public T    Value   { get; private set; }
    public long Version { get; private set; }

    public void Update(T newValue) { Value = newValue; Version++; }
}
```

Views (briefings) cache themselves keyed by the versions of the aggregates they consumed. Recompute only when a dependency has changed.

```csharp
// Pseudocode
if (foodBriefingCache.InputVersions == currentVersions)
    return foodBriefingCache.Value;

var briefing = ComputeFoodBriefing(colonists, stockpiles, map, ...);
foodBriefingCache = new Cache(briefing, currentVersions);
return briefing;
```

Lazy, pull-based. No reactive framework needed.

---

## Refresh cadence (ingestion)

Ingestion cadence is independent from minister LLM cadence. Pollers keep aggregates fresh; ministers wake on briefing-version change.

| Tier | Cadence | Examples |
|---|---|---|
| Static | Once at session start | Terrain grid, all defs, world map structure |
| Slow | Every 5–10 in-game min | Faction relations, world settlements, completed research |
| Medium | Every in-game min | Stockpiles, buildings, power grid, weather, mood |
| Fast | Every 10 in-game sec | Pawn positions, current jobs, active incidents, energy grid |
| Event-diff | Every 5 real-sec | Letters, alerts, incident list — diff against prior snapshot to detect new entries |

**No push/SSE needed.** RIMAPI's SSE endpoints stream camera video, not game events. Polling at RimWorld's timescale is sufficient — a raid letter sits in the stack for minutes. The event-diff tier detects new entries by comparing a snapshot hash or count against the previous poll.

> **M1 status:** the cadence tiers above are **not yet implemented**. M1 ships an explicit `IngestionDispatcher.RefreshAllAsync()` that pulls every needed endpoint in one parallel batch. The daily-tick poller (separate TODO) calls it once per in-game day. Tier-based pollers come online when a second consumer (M3 Food) needs faster cadence than once-a-day.

### Event-diff: what to watch
Informed by RimGPT's Harmony patch catalog — these are the signals that drive minister decisions:

| Signal | RIMAPI query | Consumer |
|---|---|---|
| New letter (raid, quest, event) | `/api/v1/incidents` or letter stack | Defense, Mayor |
| Alert added/removed (hunger, mood, health) | alerts endpoint | Chief of Staff |
| Combat log entry | battle/log endpoint | Defense |
| Pawn job change (filtered — skip haul/clean) | pawn job field on colonist list | Labor |
| Construction completion | tale / blueprint completion | Construction |
| Designation added/cancelled | designation list | Construction |
| Social interaction / opinion shift | pawn interaction log | Welfare |
| Periodic snapshot: resources, energy, research, colonist thoughts | existing medium/fast pollers | All ministers |

**Noise filter:** routine pawn jobs (haul, clean, wander) should be suppressed at the ingestion layer — they change every few seconds and carry no signal for ministers. Only surface job changes that cross a semantic threshold (pawn started firefighting, pawn downed, pawn began surgery).

---

## Briefings

Each minister gets exactly one briefing object — its complete view for reasoning. Tight, focused, ~500 tokens when serialised for the LLM. This is the primary quality lever.

### Food
```
FoodBriefing
├── DaysOfFoodRemaining         (derived: stockpile / daily consumption rate)
├── FoodStockpile               (counts by food type)
├── ActiveGrowingZones          (zone, crop, growth %, expected yield, days to harvest)
├── PlannedHarvestWindow        (when next crops mature)
├── SeasonContext               (current season, days to next, growing period remaining)
├── ColdSnapForecast            (from weather + season model)
├── HuntingTargets              (nearby huntable animals, value/risk ratio)
├── KitchenState                (cooks available, food poisoning risk level, cook bills)
├── ButcheryState               (butcher table, corpse/meat backlog, butcher bills)
├── FreezerCapacity             (used / total, current temperature, at-risk items)
├── ActiveThreats               (bool — don't plant during a raid)
└── RecentEvents                (last 24h: harvests, spoilage, food-related incidents)
```

M3 implemented `FoodBriefing` is intentionally narrower than the target above: it derives nutrition source, fallback nutrition from meal/raw counts, days-of-food, crop breakdown, ready harvest count, wild harvest candidates, wild animal count, stockpile cells, Plants/Cooking coverage, cooler/power signals, season, active threat, and recent food incident names. It does not yet compute exact food-zone yield, distance/risk scores, bill state, room temperature, spoilage timers, or caravan provisioning.

**Defense:**
```
DefenseBriefing
├── ActiveRaid                  (raid type, faction, points, composition if known)
├── PerimeterState              (wall integrity by sector, breach locations)
├── Colonists                   (shooters available, health, equipped weapons)
├── Turrets                     (active, damaged, power state)
├── Mortars                     (available, shell inventory)
├── KillboxState                (chokepoints, trap state)
├── ThreatHistory               (last 5 raids: composition, outcome, casualties)
└── SeasonalThreats             (incoming: mechanoid cluster, sieges, infestations)
```

**Construction:**
```
ConstructionBriefing
├── BuildingQueue               (outstanding blueprints, progress)
├── MaterialInventory           (steel, stone blocks, wood, components by type)
├── PowerGrid                   (total generation, consumption, batteries, deficit zones)
├── RoomProgram                 (rooms that should exist but don't)
├── StructuralRisks             (wood structures, unroofed areas, fire hazards)
├── ColonistCapacity            (beds vs colonists, growth headroom)
└── ResearchPriorities          (from Mayor posture — what to build toward)
```

**Welfare:**
```
WelfareBriefing
├── MoodDistribution            (colonists by mood tier, break-risk count)
├── NeedsSnapshot               (joy, comfort, beauty, social needs by colonist)
├── RecreationCapacity          (joy buildings available vs colonist count)
├── ApplianceState              (heaters, coolers, hospital beds)
├── RelationshipMap             (tensions, pairs at risk, recent social fights)
├── MedicalState                (wounded, sick, surgery queue — CMO sub-block)
├── ScheduleEfficiency          (work/sleep/joy balance)
└── RecentBreaks                (last 3 mental breaks: who, why, outcome)
```

**Labor:**
```
LaborBriefing
├── PawnRoster                  (name, skills, passions, current job, health, schedule)
├── BulletinBoardSnapshot       (open requests by priority, deferred count)
├── LaborUtilisation            (% of work hours productive vs idle vs forced rest)
├── SkillGaps                   (work types with no qualified pawn)
└── ShiftConflicts              (pawns double-assigned or missing from critical work)
```

---

## History — TrendBuffer

A `TrendBuffer<T>` ring buffer per tracked scalar metric. Sampled every in-game hour, retained for 7 in-game days. Used by ministers reasoning about trajectories.

```csharp
TrendBuffer<float> foodTrend;     // Food watches velocity
TrendBuffer<float> moodTrend;     // Welfare watches slope
TrendBuffer<float> wealthTrend;   // future Treasury watches velocity
TrendBuffer<int>   threatTrend;   // Defense watches raid-point growth
```

---

## The domain store holds what IS, not what it means

Derived facts — "days of food remaining," "defensive readiness level," "wealth velocity" — live in views, not in aggregates. Aggregates are raw. Views are computed. This keeps the domain clean and makes derivations independently testable.

---

## Open questions

- [ ] How is `ColonyState` shared across services? Singleton via DI? Thread-safety requirements?
- [ ] Multi-map: RIMAPI exposes caravan maps and pocket maps. MVP assumes single home map. Extend later.
- [ ] History retention: 7 in-game days is the current target. Calibrate after first playthrough.
- [ ] Should briefings include a `StaleAfter` timestamp? (i.e., don't use a briefing older than N ticks)
