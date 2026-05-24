# Willie (Construction) — Inbound Request Taxonomy & Typed-Array Request Schema

> **Design synthesis, pending human review.** This anchor formalizes the
> **inbound** request surface for the Construction minister (persona **Willie**,
> cabinet label **Construction**) and fixes the **request schema** that travels on
> `AgentFlag`. Willie is the build-feasibility owner: other ministers REQUEST builds
> from it (freezer, walls, hospital, benches, storage, power). This file owns the
> **REQUEST / INPUT** side only.
>
> **Scope boundary (do not redesign here):** the advice/output side
> (`AdviceItem`, `AdviceAction`, options, `blueprint_group`) is owned by the
> parallel anchor [`basie-advice-schema.md`](basie-advice-schema.md). A request is
> the *trigger*; the advice is the *response*. This doc maps each request onto the
> 9 canonical concerns from [`basie-advice-types.md`](basie-advice-types.md)
> but stops at the boundary — it names *which* concern fires, not *how* the
> advice is shaped.
>
> **Grounding contracts (applied, not relitigated):**
> - Requests are **advisory in MVP**: they travel on flags, allocate nothing,
>   reserve no tile, and never write to RIMAPI
>   ([`communication.md`](../Docs/design/communication.md) "Request discipline";
>   [`ministers.md`](../Docs/design/ministers.md) "Resource Requests").
> - `requested_from` names the **owner of the dependency** when known from the
>   ownership map (here, almost always `"Construction"` for inbound build asks).
> - Canonical labor work-types are a **code contract**:
>   [`Src/Common/Advice/WorkType.cs`](../Src/Common/Advice/WorkType.cs). No
>   duplicate enum is maintained in design docs.
> - `AdvicePriority` (`low`/`medium`/`high`/`critical`) is the existing priority
>   enum ([`Src/Common/Advice/AdvicePriority.cs`](../Src/Common/Advice/AdvicePriority.cs)).
>
> **Human decisions already made — APPLIED here:**
> 1. Replace the single generic `requests[]` (discriminated by `kind`) with
>    **typed arrays** (option B). Each kind carries ONLY its own fields.
> 2. **Drop `icon`** from the request record — the dashboard derives the icon from
>    kind/def.
> 3. Requests stay advisory; `requested_from` names the owner.

---

## 0. Why typed arrays replace the generic `requests[]` (rationale to record)

Today `AgentFlag.Requests` is `IReadOnlyList<ResourceRequest>?` and every entry is
**one wide record** discriminated by a `kind` enum
(`Labor | Tile | Item | Building | Bill | StockpileSpace | Attention | TradeCapacity`)
with eight mostly-nullable fields (`request, reason, quantity, priority,
requested_from, work_type, skill, icon`). This is a smell on two axes:

- **Schema smell.** A `building` request never uses `work_type`/`skill`; a `labor`
  request never uses a building class or adjacency. The wide record forces every
  consumer to know *which nullable fields apply to which kind* — an invariant the
  type system does not enforce and which drifts silently.
- **Emission smell (the load-bearing one).** The **emitting LLM** produces these.
  Telling a model "put building needs in `building_requests`, labor needs in
  `labor_requests`" is dramatically more reliable than asking it to set a `$type`
  /`kind` discriminator *and* remember which of eight nullable fields are legal for
  that discriminator. Typed arrays make the shape **self-documenting at the point
  of generation**: the array name *is* the contract, and each array's record only
  exposes fields that are always meaningful for it. Fewer illegal field
  combinations, fewer "building request with a `skill` set" nonsense rows, easier
  schema-validated decoding.

So we split the one generic array into **typed arrays + a generic catch-all**.
This also lets `building_requests` get **richer** than the old shared record could
afford — "freezer for 200 food near the kitchen by day X" must be expressible,
and stuffing that into a record shared with labor/item asks was never going to fit.

---

## 1. The typed-array request schema

Four arrays replace the single `requests[]`. Three are typed to the high-traffic
inbound kinds Willie actually receives; one is a deliberate generic catch-all.

```
flag.building_requests[]   → BuildingRequest    (rooms, walls, power, temp, benches, beacons)
flag.labor_requests[]      → LaborRequest        (work-type-qualified labor the build needs)
flag.item_requests[]       → ItemRequest         (materials/components/medicine the build consumes)
flag.attention[]           → AttentionRequest    (vague/not-yet-typed cross-minister needs)
```

Mapping from the **old** `ResourceRequestKind` members to the new world (so the
migration is unambiguous):

| Old `kind` | New home | Note |
|---|---|---|
| `Building` | `building_requests[]` | The main Willie inbound channel; now far richer. |
| `Labor` | `labor_requests[]` | Stays aligned to canonical `WorkType`. |
| `Item` | `item_requests[]` | Materials/components/meds the build consumes. |
| `StockpileSpace` | `building_requests[]` (`target_class: stockpile`/`shelf`) | Stockpile/shelf placement *is* a Construction build/zone ask; folded in, not its own array (see Open Questions). |
| `Tile` | `attention[]` | Rare; advisory-only space hint, no allocation in MVP. |
| `Bill` | `attention[]` | A bill is an **Industry/Food** production ask, not a Willie build; route as attention with `requested_from`. |
| `TradeCapacity` | `attention[]` | An **Economy** ask, never inbound to Willie; catch-all. |
| `Attention` | `attention[]` | Direct carry-over. |

### 1a. `BuildingRequest` (the centerpiece — richer than today)

This is the record other ministers fill to ask Willie for a build. It must be
expressive enough that "a freezer for ~200 units of food, next to the kitchen, by
about day 15, and I already have steel" is fully captured **without** Willie having
to guess the requester's intent.

| Field (JSON snake_case) | Type | Req? | Meaning |
|---|---|---|---|
| `request` | string | ✔ | Compact human label, e.g. `"freezer to hold winter food"`. Renders in the trace table. |
| `reason` | string | ✔ | Why the requester's domain needs it (the dependency it blocks). |
| `target_class` | enum (`BuildingClass`) | ✔ | **What kind of thing** to build — closed enum below. The single most LLM-reliable field; replaces free-text guessing. |
| `target_def` | string? | – | Optional concrete RimWorld def hint (`"Cooler"`, `"HospitalBed"`, `"Autobong"`)… requester *may* know it; Willie may override. |
| `room_class` | enum (`RoomClass`)? | – | If a *named functional room* is needed (`freezer`, `hospital`, `kitchen`, `workshop`, `research`, `bedroom`, `prison`, `recreation`, `storage`). Drives `functional_rooms`/`basic_shelter`. |
| `capacity_need` | `CapacityNeed`? | – | The sizing driver as a **typed measure**, not a raw tile count: `{ measure, amount, unit }` where `measure ∈ {beds, food_units, work_slots, storage_stacks, occupants}`. E.g. `{food_units, 200, nutrition}` or `{beds, 4, count}`. Lets Willie size the room; the requester owns the *quantity that matters*, Willie owns the *tiles*. |
| `adjacency` | `AdjacencyHint[]`? | – | Placement hints: `[{ relation, target }]` where `relation ∈ {near, inside, connected_to, away_from}` and `target` is a room/building class or named def. E.g. `near kitchen`, `away_from bedroom` (noise), `connected_to power_grid`. Distinct from owning a tile — purely a hint. |
| `power` | `PowerNeed`? | – | Whether the build implies a power draw Willie must also plan: `{ needs_power: bool, approx_watts: int? }`. Lets a freezer request implicitly raise `power_stability`. |
| `temperature` | `TempNeed`? | – | For thermal asks: `{ target_band, must_hold }` where `target_band ∈ {freezing, cold, room, sterile_warm}`. Drives `thermal_control`. |
| `materials_on_hand` | `MaterialHint[]`? | – | Requester's hint that inputs already exist: `[{ material, approx_qty }]` (e.g. `steel ~150`). Lets Willie decide `material_bottleneck` vs proceed; advisory only. |
| `urgency` | `Urgency` enum (`when_convenient`/`soon`/`before_deadline`/`blocking_now`) | – | Coarse urgency band. |
| `deadline` | `Deadline`? | – | Optional time pressure: `{ kind, value }` where `kind ∈ {by_day, by_season, before_event}` (e.g. `by_day 15`, `before_event winter`, `before_event next_raid`). The "by day X" half of the freezer example. |
| `quantity` | int? | – | Count when the ask is N of a thing (e.g. 3 hospital beds) and `capacity_need` is not the better fit. |
| `priority` | `AdvicePriority`? | – | Requester's self-rated priority (Willie may re-rate). |
| `requested_from` | string? | – | Owner of the dependency; for inbound build asks this is `"Construction"`. (Enum-vs-string is an Open Question.) |

Supporting closed enums proposed for `BuildingRequest` (final membership is a
code contract once promoted; this is the design-level set):

- **`BuildingClass`** (`target_class`): `freezer`, `wall`, `door`, `barricade`,
  `embrasure`, `power_generation`, `battery`, `conduit`, `cooler`, `heater`,
  `vent`, `bed`, `production_bench`, `research_bench`, `multianalyzer`,
  `stockpile`, `shelf`, `dumping_zone`, `trade_beacon`, `floor`, `roof`,
  `turret_platform`. *(Defense owns the turret/trap intent; Willie owns the
  buildable platform/wall feasibility — the class names the buildable, not the
  combat purpose.)*
- **`RoomClass`** (`room_class`): `freezer`, `hospital`, `kitchen`, `butcher`,
  `workshop`, `research`, `bedroom`, `barracks`, `prison`, `recreation`,
  `dining`, `storage`.
- **`CapacityNeed.measure`**, **`AdjacencyHint.relation`**, **`TempNeed.target_band`**,
  **`Urgency`**, **`Deadline.kind`** — as enumerated inline above.

**Worked example — the freezer ask, fully expressible:**

```json
{
  "request": "freezer for winter food",
  "reason": "current food will spoil before winter; nutrition chain at risk",
  "target_class": "freezer",
  "room_class": "freezer",
  "capacity_need": { "measure": "food_units", "amount": 200, "unit": "nutrition" },
  "adjacency": [{ "relation": "near", "target": "kitchen" }],
  "power": { "needs_power": true, "approx_watts": 200 },
  "temperature": { "target_band": "freezing", "must_hold": true },
  "materials_on_hand": [{ "material": "steel", "approx_qty": 150 }],
  "urgency": "before_deadline",
  "deadline": { "kind": "by_day", "value": 15 },
  "priority": "high",
  "requested_from": "Construction"
}
```

### 1b. `LaborRequest` (aligned to canonical `WorkType`)

Only the fields a labor ask needs. Kept aligned to
[`WorkType`](../Src/Common/Advice/WorkType.cs) per the ownership-map contract
(name the work-tab type; name the skill only when a threshold matters).

| Field | Type | Req? | Meaning |
|---|---|---|---|
| `request` | string | ✔ | Compact label, e.g. `"more construction labor to clear the build queue"`. |
| `reason` | string | ✔ | Why the build is starved of labor. |
| `work_type` | `WorkType` | ✔ | Canonical work-tab type (almost always `construct`; `mine`/`plant_cut`/`haul` for site prep). |
| `skill` | string? | – | Skill signal only when a threshold matters (e.g. `Construction` skill for high-quality builds). |
| `quantity` | int? | – | Optional pawn-count signal (advisory). |
| `priority` | `AdvicePriority`? | – | Self-rated. |
| `requested_from` | string? | – | `"Labor"` (deferred Auto minister) or unset — Labor is the future owner. |

> Routine hauling/cleaning must **not** become a labor request unless it is
> urgently blocking the domain (per `ministers.md` "Resource Requests").

### 1c. `ItemRequest` (materials/components the build consumes)

| Field | Type | Req? | Meaning |
|---|---|---|---|
| `request` | string | ✔ | Compact label, e.g. `"components for cooler build"`. |
| `reason` | string | ✔ | Which build the items gate. |
| `item_def` | string? | – | Concrete def hint (`"Steel"`, `"ComponentIndustrial"`, `"MedicineIndustrial"`). |
| `quantity` | int? | – | How many. |
| `priority` | `AdvicePriority`? | – | Self-rated. |
| `requested_from` | string? | – | Often `"Industry"` (make it) or `"Economy"` (buy it) — Willie surfaces the gap; another minister owns supply. |

### 1d. `AttentionRequest` (generic catch-all)

For real dependencies that **no typed array fits yet** — vague needs, rare kinds
(old `Tile`/`Bill`/`TradeCapacity`), or cross-minister nudges. Deliberately thin
so it does not become a dumping ground; promote to a typed array only with
evidence (see Open Questions).

| Field | Type | Req? | Meaning |
|---|---|---|---|
| `request` | string | ✔ | Compact label. |
| `reason` | string | ✔ | The real dependency. |
| `priority` | `AdvicePriority`? | – | Self-rated. |
| `requested_from` | string? | – | Owner if known. |

---

## 2. The `AgentFlag` change

Replace the single `requests` field with the four typed arrays. Sketch (shape is
illustrative; final C# is a code contract once the human promotes this):

```csharp
public record AgentFlag(
    string Id,
    string SourceMinister,
    FlagSeverity Severity,
    string Domain,
    string Summary,
    // REMOVED: IReadOnlyList<ResourceRequest>? Requests
    IReadOnlyList<BuildingRequest>?  BuildingRequests = null,   // "building_requests"
    IReadOnlyList<LaborRequest>?     LaborRequests   = null,    // "labor_requests"
    IReadOnlyList<ItemRequest>?      ItemRequests    = null,    // "item_requests"
    IReadOnlyList<AttentionRequest>? Attention       = null,    // "attention"
    string? Detail = null,
    DateTimeOffset? ExpiresAt = null);
```

Notes:
- All four arrays are **nullable/optional** — most flags carry none or one.
- `FlagSeverity` (the flag's own severity) is unchanged and stays distinct from a
  request's `priority` (the requester's per-ask rating). They answer different
  questions: flag severity = how loud the *issue* is to the cabinet; request
  priority = how urgent *this dependency* is to the requester.
- The old `ResourceRequest`/`ResourceRequestKind`/`IconRef` records are retired
  from the flag path. (Whether `ResourceRequest` survives elsewhere — e.g.
  advice-side — is the advice anchor's call, not this doc's. The `kind`
  discriminator is gone from the *flag request* surface either way.)

---

## 3. Inbound ask-map → arrays/fields → triggered concern

Each requester's asks, the array+fields they fill, and which canonical Willie
concern ([`basie-advice-types.md`](basie-advice-types.md)) the request
triggers. **Food is the only LIVE requester this milestone; all others are
design-forward.**

| Requester | Inbound ask | Fills array → key fields | Triggers Willie concern |
|---|---|---|---|
| **Food (LIVE)** | Cooler-backed freezer | `building_requests` → `target_class:freezer`, `room_class:freezer`, `capacity_need:{food_units}`, `temperature:{freezing}`, `adjacency:[near kitchen]`, `deadline` | `thermal_control` |
| **Food (LIVE)** | Power for the cooling load | `building_requests` → `target_class:power_generation`/`battery`, `power:{needs_power,approx_watts}` | `power_stability` |
| **Food (LIVE)** | Food stockpile / shelf placement | `building_requests` → `target_class:stockpile`/`shelf`, `room_class:storage`, `adjacency:[near kitchen, inside freezer]` | `storage_placement` |
| **Food (LIVE)** | Kitchen / butcher adjacency | `building_requests` → `room_class:kitchen`/`butcher`, `adjacency:[near freezer]` | `base_layout` (or `functional_rooms` if the room is absent) |
| **Food (LIVE)** | Cooking building (stove) | `building_requests` → `target_class:production_bench`, `room_class:kitchen` | `functional_rooms`; `material_bottleneck` if `materials_on_hand` shows a gap; `stalled_builds` if a placed stove blueprint stalls |
| **Defense** | Walls / doors / barricades / embrasures | `building_requests` → `target_class:wall`/`door`/`barricade`/`embrasure` | `basic_shelter` (enclosure) / `fire_risk` (material+spacing) |
| **Defense** | Trap + turret build platforms | `building_requests` → `target_class:turret_platform`/`wall`; intent stays Defense-owned | `functional_rooms`/`base_layout` for the buildable; Defense owns combat purpose |
| **Defense** | Fortify a weak entry | `building_requests` → `target_class:wall`/`barricade`, `adjacency:[connected_to perimeter]`, `urgency:blocking_now` | `fire_risk` (breach/burn dominant) |
| **Defense** | Breach / fire build response | `building_requests` (rebuild) + `attention` (active-threat context) | `stalled_builds` / `fire_risk` |
| **Welfare** | Bedrooms | `building_requests` → `room_class:bedroom`, `capacity_need:{occupants}`, `target_class:bed` | `basic_shelter` (floor) → `functional_rooms` (quality) |
| **Welfare** | Room quality / impressiveness | `building_requests` → `room_class`, `capacity_need`, quality via `reason` | `functional_rooms` |
| **Welfare** | Rec + dining room | `building_requests` → `room_class:recreation`/`dining` | `functional_rooms` |
| **Welfare** | Comfort temperature systems | `building_requests` → `target_class:heater`/`cooler`, `temperature:{room}` | `thermal_control` (thermal-asset family) / `power_stability` |
| **Medical** | Hospital room + beds | `building_requests` → `room_class:hospital`, `target_class:bed`, `capacity_need:{beds}` | `functional_rooms` |
| **Medical** | Sterile floor | `building_requests` → `target_class:floor`, `room_class:hospital`, `reason:sterile` | `functional_rooms` |
| **Medical** | Hospital power / temp | `building_requests` → `power:{…}`, `temperature:{sterile_warm}` | `power_stability` / `thermal_control` |
| **Medical** | Medicine stockpile placement | `building_requests` → `target_class:stockpile`/`shelf`, `room_class:hospital`, `adjacency:[inside hospital]` | `storage_placement` |
| **Industry** | Production benches | `building_requests` → `target_class:production_bench`, `room_class:workshop` | `functional_rooms` |
| **Industry** | Material / component stockpile near workshops | `building_requests` → `target_class:stockpile`, `adjacency:[near workshop]` | `storage_placement` |
| **Industry** | Industry power | `building_requests` → `target_class:power_generation`, `power:{…}` | `power_stability` |
| **Research** | Research bench / multianalyzer + room | `building_requests` → `target_class:research_bench`/`multianalyzer`, `room_class:research` | `functional_rooms` |
| **Research** | Research room power | `building_requests` → `power:{…}` | `power_stability` |
| **Economy** | Trade beacon | `building_requests` → `target_class:trade_beacon`, `adjacency:[near storage]` | `storage_placement` / `functional_rooms` |
| **Economy** | Trade-good storage | `building_requests` → `target_class:stockpile`/`shelf`, `room_class:storage` | `storage_placement` |
| **Economy** | Caravan packing space | `attention` (no buildable owned by Willie; advisory) + wealth-discipline `reason` context | `attention` → `base_layout` if it surfaces a real space loop |
| **All** | Build needs labor to progress | `labor_requests` → `work_type:construct` (+ `skill`) | feeds `stalled_builds` |
| **All** | Build needs materials to progress | `item_requests` → `item_def`, `quantity` | feeds `material_bottleneck` / `stalled_builds` |

This covers every inbound ask named in the milestone scope and the "Base And
Infrastructure" rows of the [`ministers.md`](../Docs/design/ministers.md) Action
Ownership Map. Construction always owns the *buildable feasibility/placement*; the
requester always owns *why it matters* (`reason`).

---

## 4. `icon` dropped (note)

`IconRef icon` is **removed** from the request record(s). The old
`ResourceRequest.Icon` was redundant with what the dashboard can derive: the icon
for a request is a function of its **kind** (which array it sits in) and its
**def** (`target_def`/`item_def`/`target_class`). Carrying an explicit icon on the
request asked the emitting LLM to pick a render asset — outside its competence and
a source of wrong/empty icons. The dashboard owns icon resolution from
kind/def at render time. (This mirrors the human decision; the advice-side anchor
should consider the same removal on its action record, but that is its call.)

---

## 5. Open questions

- [ ] **Rarely-used kinds: own array or stay in `attention[]`?** This doc folds
      `Tile`, `Bill`, `TradeCapacity` into `attention[]` (none are genuine inbound
      *build* asks). Should any earn a typed array later — e.g. if `Bill` traffic
      to Industry/Food becomes high-volume — or do they belong on the *advice*
      side / a different minister's request surface entirely?
- [ ] **`requested_from`: string or enum?** A closed `MinisterRef` enum would make
      routing/validation safer and is more LLM-reliable (closed set), but a string
      tolerates modded/future ministers and matches today's field. Recommendation:
      enum once the minister roster is frozen; string until then. Decide alongside
      `SourceMinister` on `AgentFlag` (same question, should match).
- [ ] **Should `StockpileSpace` really fold into `building_requests`?** It is a
      build/zone placement ask (Construction owns the zone per the Zone Ownership
      table), so it fits — but a thin `stockpile_requests[]` could be cleaner if
      stockpile/shelf asks dominate volume. Folded for now; revisit with data.
- [ ] **`capacity_need.measure` enum membership.** Proposed
      `{beds, food_units, work_slots, storage_stacks, occupants}` — is that the
      complete set the live + design-forward requesters need, or do hospital
      sterility / research multianalyzer slots need their own measure?
- [ ] **One `building_requests[]` entry per flag, or many?** A single Food flag
      could ask for freezer + power + stockpile at once (three entries) vs three
      separate flags. Multiple entries keep the dependency graph in one place;
      separate flags dedupe/supersede independently. Lean toward **multiple
      entries per flag** for a coherent ask, but confirm against the flag
      dedupe-key open question in [`communication.md`](../Docs/design/communication.md).
- [ ] **Does `deadline.kind: before_event` need a closed event vocabulary?**
      (`winter`, `next_raid`, …) — or stay free-text? A closed set is more
      reliable but must be kept in sync with game-state signals Willie can actually
      observe.
