# Minister of Construction — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M5 — feeder advisor. Note: placement / layout decisions are explicitly deferred (Base Layout Minister candidate).
>
> ⚠️ **Pivot translation needed.** Pre-pivot `goal_id` / HTN vocabulary maps to `advice_type` under the assisted-gameplay model ([`../../DESIGN.md`](../../DESIGN.md)). Full rewrite when the M5 slice opens.

---

## Domain

Buildings, power infrastructure, build order. What to build and in what priority. Does NOT currently decide where to build — placement uses heuristics until Base Layout Minister exists.

Research priority queue lives here (as a module) until Research Director graduates.

---

## goal_id enum

```csharp
public enum ConstructionGoal
{
    ExecuteBuildQueue,        // build outstanding blueprints
    ExpandPower,              // solar, wind, geothermal, batteries
    BuildRoomProgram,         // rooms that should exist but don't
    RepairStructures,         // damaged buildings, breached walls
    UpgradeMaterials,         // replace wood/low-quality with stone/steel
    SetResearchPriority,      // update research queue from Mayor posture
    PrepareForExpansion       // pre-build infrastructure ahead of need
}
```

---

## Briefing

Key fields:
- `BuildingQueue`: outstanding blueprints, % complete, blocked reasons
- `MaterialInventory`: steel, stone blocks, wood, components, plasteel, gold by count
- `PowerGrid`: generation (W), consumption (W), battery storage, deficit zones by sector
- `RoomProgram`: rooms that should exist (derived from colonist count + Mayor objective)
- `StructuralRisks`: wood structures near fire hazards, unroofed areas, missing doors
- `ResearchQueue`: current research + prioritised queue (from Mayor posture)
- `ColonistCapacity`: beds vs colonists, free space for growth

---

## Rules layer (target ~70% coverage)

| Rule | Condition | Output |
|---|---|---|
| `repair_breach` | Any wall breach detected | RepairStructures (High) |
| `power_deficit` | PowerGrid.Deficit > 0 | ExpandPower (High) |
| `low_battery_backup` | Battery capacity < 2x nightly consumption | ExpandPower (Medium) |
| `queue_exists` | BuildingQueue has items AND materials available | ExecuteBuildQueue |
| `missing_beds` | Beds < Colonists | BuildRoomProgram (beds, High) |
| `wood_structure_risk` | Any wood wall adjacent to open flame source | UpgradeMaterials (Medium) |
| `stonecutting_priority` | MaterialInventory.StoneBlocks < 200 AND stonecutter exists | ExecuteBuildQueue (stonecutting bills) |

**Escalates when:**
- Room program decisions (which rooms, in what order, what size)
- Power architecture decisions (geothermal placement, solar farm layout)
- Research priority trade-offs
- Expansion planning
- Material substitution trade-offs

---

## Placement heuristics (until Base Layout Minister exists)

**TODO** — this is explicitly deferred. MVP placeholder rules:
- Extend along existing walls
- Power sources near the power grid center
- Freezer adjacent to kitchen
- Hospital adjacent to bedrooms
- Killbox at map entry chokepoint

These will produce mediocre layouts. That's acceptable for MVP. The Base Layout Minister is a candidate for a dedicated session.

---

## Research module

Construction hosts research priority management until Research Director graduates. 

Research priority queue is a simple ordered list derived from:
- Mayor's `colony_objective` (e.g., `ship_launch` → prioritise fabrication chain)
- Current threat level (raids incoming → Machining, Gun Turrets)
- Missing room program items (hospital bed not built → Hospital research first)

No LLM needed for routine research ordering. Escalate when trade-offs aren't clear from posture.

---

## RIMAPI writes owned

- Blueprint placement (all building types)
- Deconstruct designation
- Research queue management
- Mining designation (when building new rooms requires excavation)
- Stockpile zone for materials

Labor requests posted:
- Construct, haul materials, mine, deconstruct (Construction + Hauling labor)

---

## HTN domain

```
ExecuteBuildQueue
├── materials_available → PostLaborRequest(Construction, Build, nextItem)
├── materials_low → PostLaborRequest(Hauling, StockpileMaterials)
└── blocked_by_power → ExpandPower first

ExpandPower
├── geothermal_available → BlueprintGeothermal, PostLaborRequest(Construction, Build)
├── no_geothermal → BlueprintSolar + Battery, PostLaborRequest(Construction, Build)
└── escalate → LLM (power architecture decisions)

BuildRoomProgram
├── next_room_clear → BlueprintRoom(heuristic placement), PostLaborRequest(Construction, Build)
└── escalate → LLM (room type trade-offs, layout decisions)
```

---

## Success metrics

- Power grid never in deficit for > 1 in-game day (excluding solar flares).
- All colonists have a bed within 3 in-game days of joining.
- Stone walls cover all exterior surfaces by day 30.
- Build queue cleared within 5 in-game days of posting.

---

## Open questions / TODO

- [ ] Placement / layout: when does Base Layout Minister get designed? What are the promotion triggers?
- [ ] How does Construction handle Defense's fortification requests? (Defense designates; Construction builds. Interface TBD.)
- [ ] Research Director graduation criteria: when does research trade-off reasoning justify its own minister?
- [ ] How does Construction know what rooms are "missing"? (RoomProgram derivation — define in state-store session)
- [ ] Component bottleneck: Construction needs to manage component fabrication. Is that Construction or Labor?
