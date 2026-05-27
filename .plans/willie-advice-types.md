# Willie (Construction) — Canonical Concern Set

> **Reconciliation note.** This file resolves the conflicting concern lists in
> [`base-construction-layout-agent.md`](base-construction-layout-agent.md) (8 concerns)
> and [`base-layout-construction-tips.md`](base-layout-construction-tips.md) (9 concerns)
> into ONE canonical set for the Construction minister (persona **Willie**, cabinet
> label **Construction**). It is design synthesis pending human review; the human
> promotes the final list into
> [`Docs/design/ministers/construction.md`](../Docs/design/ministers/construction.md),
> whose concerns are currently an open question.
>
> Grounding contracts (do not relitigate, applied here):
> - `concern` is a **closed per-minister enum** and is the unit the autonomy
>   dial graduates **one at a time** ([`advice.md`](../Docs/design/advice.md) "Concern").
> - `actions[]` (e.g. `place_blueprint`, `set_stockpile_zone`) are **separate
>   concrete operations**, not concerns. `PlaceBlueprint` is a real
>   `AdviceActionKind` in `Src/Common/Advice/AdviceAction.cs`.
> - Mirror the Food enum style (`Src/Common/Advice/FoodAdviceType.cs`): a granular,
>   execution-facing, per-minister closed C# enum, snake_cased on the JSON wire.
> - Inbound-request ownership is taken from the Construction issue catalogue in
>   [`deterministic-cos-cabinet-issue-solver.md`](deterministic-cos-cabinet-issue-solver.md)
>   (Construction section). Food is the only LIVE requester this milestone; the
>   rest are design-forward.

---

## 1. Canonical Concern List (8 concerns)

Each entry: snake_case enum value + one-line definition + which inbound minister
request(s) it serves. "LIVE" marks a request path that exists at this milestone;
all others are design-forward.

| `concern` | Definition | Inbound requester(s) it serves |
|---|---|---|
| `power_stability` | Net power deficit, no/low battery backup, or a fragile load margin that endangers critical consumers. | Food **(LIVE)** — coolers/freezer draw power; Defense (turrets), Medical (hospital), Industry (benches) when those scope live. |
| `thermal_control` | Build/repair the buildable thermal envelope — cooler, walls, vents, power, and placement — behind a refrigeration need. Covers cooling assets generally (extends to hospital/heat shells as more ministers go live). | Food **(LIVE)** freezer/cooler/cold-room request; later Medical (sterile/temperature-controlled shell), Welfare (heat/cold safety). |
| `functional_rooms` | A *named functional room* the colony is missing or that is undersized/wrong-purpose for its job (bedroom, hospital, prison, workshop, research room, recreation), beyond the survival floor. | Welfare (bedrooms / barracks / survival-floor enclosure; recreation; quality), Medical (hospital), Research (research room), Industry (workshop) — all design-forward. |
| `storage_placement` | Stockpiles/shelves are mis-placed relative to the work that consumes or produces them — benches without input storage, materials far from the build queue, food storage split from kitchen/butcher. | Food **(LIVE)** food-storage-to-kitchen proximity; Industry (bench input/output flow) design-forward. Construction owns the physical placement; the requester owns *why* the throughput matters. |
| `material_bottleneck` | Steel, wood, stone blocks, components, or stone chunks are too low to satisfy visible/queued build demand. | Food **(LIVE)** (materials gating a freezer/stove build); Defense, Medical, Industry, Research design-forward. Often pairs with an Industry/Economy flag request. |
| `fire_risk` | Wood-heavy critical rooms where stone/material is available, or insufficient firebreak spacing between structures. Material risk **and** spacing risk. | Self-derived (freezer/kitchen/power/hospital/storage/bedroom rooms); Defense for active-threat/fire context design-forward. |
| `stalled_builds` | Existing blueprints/frames cannot progress — missing materials at the site, unreachable work, or a pending unmet cross-minister request. | Any minister whose requested build has stalled. Food **(LIVE)** when a requested freezer/stove blueprint is stuck. |
| `base_layout` | High-frequency work loops are wasting pawn travel (named route chains: field→freezer→kitchen→dining, storage→bench, material→build site). Also the home for whole-base topology signals (see §2). | Food **(LIVE)** food-chain loop length; cross-cutting for all ministers' work loops. |

This is the smallest non-overlapping set that still covers every inbound need
named in the CoS Construction catalogue while staying granular enough to be
autonomy-dial units. Eight types (`basic_shelter` moved to Welfare 2026-05-27
— see §4.5).

### Mapping to the CoS Construction issue catalogue

The CoS catalogue lists six Construction issue *families*; this enum is a
slightly finer execution-facing view of the same domain, which is correct —
issue families group for routing, concerns graduate for autonomy:

| CoS issue family | Canonical concern(s) |
|---|---|
| Power deficit | `power_stability` |
| Missing basic rooms | `functional_rooms` (bedroom / barracks / survival-floor enclosure folded in; trigger lives in Welfare) |
| Build queue blocked | `stalled_builds` |
| Material bottleneck | `material_bottleneck` |
| Temperature asset need | `thermal_control` |
| Structural or fire risk | `fire_risk` |
| (layout — not yet a CoS family) | `storage_placement`, `base_layout` |

---

## 2. `base_topology` decision

**Decision: DROP `base_topology` as its own concern. Fold it into
`base_layout` (with a `base_topology` *dashboard grouping / briefing
signal cluster* underneath).**

Rationale:
- The tips plan itself files this exact question as an Open Follow-Up ("Decide
  whether `base_topology` is its own first-slice concern or a dashboard
  grouping under `base_layout`"). It was never a settled concern.
- Topology (circulation spine, quadrant/expansion capacity, critical-room depth
  from perimeter, perimeter/door layers) is **the same player-facing issue as
  layout efficiency** — both say "your spatial arrangement is costing you," and
  both surface as Suggest-mode "build/move the next thing toward a coherent
  shape." Splitting them gives the autonomy dial two knobs the player would
  almost always set together, which violates the "smallest non-overlapping set"
  goal and adds a thin concern with no distinct execution surface.
- The signals stay valuable, so they survive as a **briefing/dashboard grouping**
  (the tips plan already proposes a `Base Topology` dashboard panel: main paths,
  quadrants, critical-room depth, perimeter layers). Dashboard groupings are not
  concerns, so this costs nothing on the closed enum.
- Where topology overlaps a *different* concern, route to that type instead: a
  perimeter-adjacent critical room is `fire_risk` (or a Defense-led fortification
  request) when the dominant risk is breach/burn, not a separate topology card —
  this matches the tips plan's own "emit `base_topology` **or** `fire_risk`
  depending on the dominant risk" hedge, which we resolve in favor of the
  concrete-risk type.

If, after real play, topology advice proves to need its own autonomy graduation
(player wants travel hints ON but topology rebuilds OFF), promote it then — with
evidence — exactly as `advice.md` prescribes for adding a concern.

---

## 3. `functional_rooms` vs `storage_placement` decision

**Decision: KEEP BOTH. Do not merge.**

Rationale — they answer different questions and serve different requesters:
- `functional_rooms` is about **room existence / purpose / sizing** — "you have no
  hospital," "your prison is also your barracks," "the workshop is too small for
  its benches." Its requesters are Welfare, Medical, Research, Industry (rooms
  with a *function*). Survival-floor bedrooms/barracks (the former
  `basic_shelter`) fold in here: same `place_blueprint` action surface, same
  room-class taxonomy, same `BuildingRequest` shape from Welfare.
- `storage_placement` is about **material-flow placement** — stockpiles/shelves
  in the wrong *spot* relative to the work that uses them. It is a Food (LIVE)
  and Industry concern about travel/throughput, not about whether a room exists.
  The tips plan explicitly frames this as a material-flow issue Construction owns
  because it sees physical placement, distinct from zoning purpose.
- Merging them would force one autonomy-dial knob to govern both "decide the
  colony needs a hospital" (a structural, judgment-heavy, LLM-escalation call)
  and "the steel shelf is 30 tiles from the smithy" (a cheap deterministic
  proximity lint). Those have very different blast radius and confidence, so they
  must graduate independently.

Note on the layout-agent vs tips diff this resolves: layout-agent had
`functional_rooms` but no `storage_placement`; tips had `storage_placement` but
dropped `functional_rooms`. Neither was right to drop the other — they are
orthogonal. The canonical set restores both and removes only `base_topology` as
a standalone (§2).

---

## 4. `build_structure` dropped — why

There is **no generic `build_structure` concern**, and there must not be.

- `concern` is the unit the autonomy dial graduates one at a time and must
  stay **granular and execution-facing** (per `advice.md` and the Food enum). A
  catch-all "build a structure" would be the opposite: it would let the player
  graduate "Construction may build *anything*" in one dial click, which is
  exactly the over-broad autonomy the per-concern design exists to prevent.
- "Build a structure" is an **ACTION, not a category.** The placement primitive
  already exists as `AdviceActionKind.PlaceBlueprint` (`place_blueprint`) in
  `Src/Common/Advice/AdviceAction.cs`, alongside `set_stockpile_zone` and
  `designate_zone`. Every canonical concern above emits `place_blueprint`
  (and friends) in its `actions[]`; the *concern* names the **reason/category**
  (why this build matters — power, shelter, fire, flow), the *action* names the
  **operation** (place this blueprint here). `advice.md` draws this line
  explicitly: actions are concrete "do X" operations, concerns are the closed
  per-minister category enum.

So: `build_structure` collapses the reason and the operation into one mushy token
and breaks both the autonomy model and the action/concern separation. Dropped.

---

## 4.5 `basic_shelter` moved to Welfare — why

**Decision (2026-05-27): DROP `basic_shelter` from Willie's canonical concern set.
The colonists-vs-beds and unenclosed-sleeping triggers belong to Welfare. Willie
sees this concern only through an inbound `BuildingRequest { target_class: bed |
room_class: bedroom/barracks }`, which routes through `functional_rooms` (same
shape as every other "build me a room" request).**

Rationale — the same ownership rule that places `thermal_control` (Food owns
the cooler trigger; Willie places) places this concern in Welfare:

- **Trigger = mood / survival floor.** "Colonists have no beds" or "colonists
  sleep under open sky" is a needs / mood / safety state, not a placement
  problem. Per [`Docs/design/ministers/welfare.md`](../Docs/design/ministers/welfare.md)
  Welfare owns the **Mood & Needs** domain; sleep need and bed comfort live
  there.
- **Symmetry with the other 8 concerns.** Every concern in §1 is named by the
  *category of reason* a build matters; the requester owns the *why*, Willie
  owns the *where*. `basic_shelter` was the lone holdout that bundled both —
  "self-derived from colony size" was Willie reading Welfare's domain.
- **Granularity.** Once Welfare exists, the survival-floor bed ask and the
  quality-bedroom ask are the same `BuildingRequest` from the same requester,
  just at different `priority`/`urgency`. Splitting them across two Willie
  concerns adds a thin enum value with no distinct execution surface — exactly
  the smell §2 rejected for `base_topology`.
- **Bootstrapping.** Welfare is design-forward; it does not LIVE-emit requests
  yet. So is half the §1 table. `basic_shelter` joins the design-forward set;
  no LIVE behavior is lost (no Construction-side `basic_shelter` rule had
  shipped).

Where the survival-floor signals go:

- **Trigger signals** (`colonists > beds`, `unenclosed_sleeping`,
  `missing_door` on a bedroom) → Welfare briefing, when Welfare scopes.
- **Build resolution** → Willie's `functional_rooms` concern via
  `BuildingRequest { room_class: bedroom | barracks }`. The `place_blueprint`
  surface and `BlueprintGroup` payload are unchanged.
- **Per-room boundary-cell / door reads** (the `rimapi-room-entry-cells`
  HumanTodo) still serve Willie's `AnchorInventory.EntryCells[]`; the future
  `missing_door` rule (if Welfare wants one) reads the same endpoint.

If real-play evidence later shows survival-floor builds need a distinct
autonomy graduation from quality-bedroom builds, promote it then — exactly as
`advice.md` prescribes for adding a concern. Until then the absence of
`basic_shelter` from the closed enum is the lighter choice.

---

## 5. First-slice rules vs later

Mirrors the layout-agent slicing (Slice A = rules-only skeleton; Slice C =
LLM/RAG escalation) and the data-availability caveats in both source plans.
"Rules-first (Slice A)" = deterministic, ships from the briefing alone. "LLM /
later" = needs escalation judgment or data not yet derivable from current RIMAPI.

**Ship as rules-first deterministic advice in Slice A (data likely available):**

- `power_stability` — net = production − consumption is a simple aggregate;
  threshold rule. Strongest day-one candidate.
- `material_bottleneck` — resource counts vs visible/queued demand; threshold
  rule.
- `thermal_control` — fires off a **live Food flag** requesting
  cooler/freezer when rough materials exist; the trigger is the inbound flag, so
  it is deterministic even before rich thermal data lands.
- `stalled_builds` — blueprint/frame backlog + missing-material-at-site is
  derivable once the building/blueprint read exists; deterministic threshold.
- `fire_risk` — **partial in Slice A:** the *material* half (wooden walls in a
  classified critical room + stone blocks available) is a deterministic rule and
  depends on the building-condition/material read (HumanTodo
  `source-todo-building-condition-read`). The *spacing* half (firebreak gaps
  between structures) needs inter-structure distance data and may slip later.

**Needs the LLM or data not yet available (Slice C / later):**

- `base_layout` (incl. the folded `base_topology` signals) — needs named
  route-chain distances and circulation/topology derivations that are not yet in
  the state store; a coarse single-loop rule (e.g. food-chain length) can ship
  early, but the multi-loop / topology judgment is LLM-escalation per the tips
  plan's "competing expansion directions / flat-base trade-offs."
- `storage_placement` — needs bench↔input-storage and stockpile↔build-queue
  proximity data not yet derivable; a single coarse rule
  (food-storage-to-kitchen distance) could ship early off Food-adjacent data,
  but the general case is later.
- `functional_rooms` — inherently escalation-heavy ("does the colony need a hospital
  now?" is a posture/phase judgment). Requires room-purpose/quality derivation
  (HumanTodo `source-todo-room-quality-read`) and is an explicit Escalation
  Boundary in `construction.md`. LLM, later.

Empty-state and escalation behavior follow Food: a clean base publishes a
successful **empty** snapshot (trace e.g. `maintain_build_program`); genuinely
ambiguous competing build needs escalate to the LLM rather than firing a noisy
rule.

---

## 6. Action(s) per concern

What each concern emits as player-facing `actions[]`. Build concerns route through the
**Placement Solver** → a `place_blueprint` action that gets an executable Apply
only when the Solver produced a fork-validated `blueprint_group` (else prose-only).

| concern | Action(s) | Apply? |
|---|---|---|
| `power_stability` | `place_blueprint` (generator/battery) | yes (1-asset group) |
| `thermal_control` | `place_blueprint` (freezer group, `options[]`) | yes |
| `functional_rooms` | `place_blueprint` (room, `options[]`) — includes survival-floor bedrooms/barracks since `basic_shelter` folded here | yes |
| `storage_placement` | `place_blueprint` (shelf) / `set_stockpile_zone` | blueprint yes; zone = Suggest-only knob |
| `fire_risk` | `place_blueprint` (stone rebuild) + `note` (firebreak) | rebuild yes |
| `material_bottleneck` | `note` + `RequestResource` (labor/item) | no — non-placement |
| `stalled_builds` | `note` + `RequestResource` (unblock) | no — blueprint already exists |
| `base_layout` | `place_blueprint` (relocate/add) + `note` | sometimes |

`place_blueprint` actions carry an Apply via the `place_blueprint_group` payload
once the Placement Solver + fork endpoints exist (see
[`placement-solver.md`](placement-solver.md), [`willie-advice-schema.md`](willie-advice-schema.md) §2).
`set_stockpile_zone` is a high-blast-radius policy knob → Suggest-only, no early Apply.
`material_bottleneck` / `stalled_builds` are non-placement: a `note` plus a
flag `RequestResource` (item/labor), no Apply.
