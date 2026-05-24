# Willie (Construction) — Advice / Output Schema

> **Agent-created design doc.** Synthesis pending human review. Scope is the
> **advice / output side** of the schema only: `AdviceItem`, `AdviceAction`,
> the per-kind apply payloads, a new multi-option concept, and a
> `blueprint_group` placement payload. The **request / flag side**
> (`AgentFlag`, `ResourceRequest`) is owned by the parallel
> `.plans/willie-request-taxonomy.md` — referenced at the boundary here, not
> redesigned.
>
> Grounding contracts applied (do not relitigate):
> - MVP posture is **suggest + player-confirmed *partial* apply**, not
>   suggest-only ([`DESIGN.md`](../Docs/DESIGN.md) decision "MVP posture is
>   suggest + player-confirmed *partial* apply"). Every write is gated by an
>   explicit player click; this is **not** `Auto`. A `place_blueprint` and a
>   multi-asset `blueprint_group` are legitimate Apply candidates behind a
>   player click.
> - Icons are **render-hints** the dashboard derives from kind/def
>   ([`advice.md`](../Docs/design/advice.md): "Icon refs are rendering hints
>   only; they are not execution inputs").
> - Explanation lives at **advice level** (`body` / `rationale`); actions stay
>   compact UI/action primitives ([`advice.md`](../Docs/design/advice.md)
>   "Actions").
> - The LLM never chooses raw endpoints, payloads, or arbitrary target ids.
>   Deterministic code decides whether an action can expose Apply
>   ([`advice.md`](../Docs/design/advice.md) "Assisted Apply").
> - Source of record for exact C# / JSON shape stays in `Src/Common/Advice/`;
>   this doc records the contract and rationale, per the
>   [`DESIGN.md`](../Docs/DESIGN.md) "Design docs should not mirror code" rule.

Current shapes read for this doc: `Src/Common/Advice/AdviceItem.cs`,
`Src/Common/Advice/AdviceAction.cs` (incl. `AdviceActionApply`,
`AdviceApplyKind`, `AdviceActionKind`, `AssistedApplyLimits`),
`Src/Common/Aggregates/Snapshots.cs` (`MapRect` = `x1/z1/x2/z2`,
`MapPosition` = `X/Y/Z`), `Src/Common/Advice/ResourceRequest.cs` (`IconRef`).
RIMAPI DTOs from [`rimapi-blueprint-placement-endpoint.md`](rimapi-blueprint-placement-endpoint.md).

---

## 1. Flatten changes to `AdviceAction`

`AdviceAction` is a compact UI/action primitive. Two fields are removed.

| Change | Field | Rationale (one line) |
|---|---|---|
| **DROP** | `icon` (`IconRef?`) | Icons are render-hints the dashboard derives from `kind`/def per `advice.md`; carrying an explicit icon ref on every action duplicates a rendering decision and is never an execution input. |
| **DROP** | `reason` (`string?`) | Redundant with `AdviceItem.rationale` / `body`; `advice.md` says explanation lives at advice level and action instructions "should scan like compact UI/action primitives." |

**Kept** on `AdviceAction`: `kind`, `instruction`, optional `quantity`,
`owner`, `work_type`, `skill`, and `apply`. (`work_type`/`skill` stay because
`advice.md` mandates a RimWorld work-tab type on labor-like actions, and they
are routing-relevant, not prose.)

> **Note (parser/tolerance):** dropping fields from the *emit* contract does not
> require breaking the tolerant inbound parser. A legacy/LLM payload that still
> carries `icon` / `reason` on an action should be silently ignored on those two
> keys, not rejected — consistent with `advice.md` "Tolerant parsing." This is a
> parser detail flagged for the implementer, not a schema field.

---

## 2. Per-kind apply-payload split

**Decision applied:** split the `AdviceActionApply` god-record (11 nullable
fields serving 4 apply kinds) into **per-kind payloads**, each carrying only
its own fields. This removes the "which of these 11 nullables is live for this
kind?" ambiguity that makes validation and the dashboard guess.

### 2.1 Shared apply envelope (common to every kind)

These four fields are genuinely shared and stay on a small common header
(every apply, regardless of kind, needs them for display + revalidation):

| field | type | notes |
|---|---|---|
| `kind` | `AdviceApplyKind` | discriminator; selects the payload variant |
| `label` | `string` | Apply-button label (e.g. "Place freezer shell") |
| `target_summary` | `string` | one-line human summary of what will be written |
| `map_id` | `int` | required for revalidation against fresh state |

The variant payload is carried under a discriminated `payload` (or, in C#, a
sealed-hierarchy `AdviceApplyPayload` with one subtype per kind). `target_count`
moves *into* the variants that have a meaningful count (designations / things /
group asset count); it is not universal (a single bill upsert has no list).

### 2.2 Apply kinds + the fields each carries

Existing 4 kinds (from `AdviceApplyKind`) re-expressed as tight payloads, plus
the new blueprint/group kind:

| `AdviceApplyKind` | Maps to action kind(s) | Payload fields (its own only) | Limit (from `AssistedApplyLimits`) |
|---|---|---|---|
| `mark_harvest_area` | `mark_harvest` | `rect: MapRect`, `target_ids: string[]`, `target_count: int` | `MaxHarvestTargets` 80 / `MaxHarvestRectArea` 120 |
| `mark_hunt_area` | `mark_hunt` | `rect: MapRect`, `target_ids: string[]`, `target_count: int` | `MaxHuntTargets` 12 / `MaxHuntRectArea` 120 |
| `unforbid_things` | `unforbid` | `thing_ids: string[]` **or** `thing_targets: AdviceThingApplyTarget[]`, `target_count: int` | `MaxUnforbidTargets` 50 |
| `upsert_production_bill` | `production_bill` | `workbench_building_id: string`, `recipe_selector_key: string`, `repeat_mode: string`, `target_count: int` (do-until bound) | `MaxProductionBillTarget` 50 |
| **`place_blueprint_group` (NEW)** | `place_blueprint` | `blueprint_group: BlueprintGroup` (see §4), `asset_count: int` | new — see §4 limits note |

Notes:
- A **single** blueprint placement is just a `BlueprintGroup` of length 1 — we
  do **not** add a separate `place_blueprint_single` apply kind. One payload
  type keeps the apply path (and the RIMAPI-call loop) uniform; the RIMAPI places
  per-asset anyway (`rimapi-blueprint-placement-endpoint.md` §3.2 is per-asset).
- `AdviceThingApplyTarget` is unchanged and used only by `unforbid_things`.
- `MaxMissingTargetFraction` (0.25) stays a cross-kind revalidation tolerance
  for the id-bearing kinds (harvest/hunt/unforbid), not a payload field.

### 2.3 Eligibility for `place_blueprint_group` Apply (trust boundary)

Per `advice.md` "Assisted Apply", deterministic code — not the LLM — decides
whether a `place_blueprint` action exposes Apply. A group is Apply-eligible only
when **every** asset:
- resolves to a known `def_name` (+ `stuff_def_name` when stuffed),
- has caller-supplied absolute cell/rect (no server-side cell resolution — the
  RIMAPI plan §1/§8 explicitly excludes server cell resolution),
- passes the RIMAPI **validate** dry-run (`can_place:true`) against fresh state,
- is idempotent on retry (RIMAPI returns `already_present` / `already_built`).

This keeps blueprint placement a **targeted designation** (additive, ephemeral —
the game clears the blueprint once built) per the `advice.md` "Policy Knobs vs.
Targeted Designations" test, *not* a policy knob. It is therefore a legitimate
allowlist candidate even though it writes, exactly as the reframed MVP posture
permits.

---

## 3. `AdviceOption` + `AdviceItem.options[]`

**Decision applied:** add optional multi-option advice. Some Construction advice
(layout, freezer/room placement) is better expressed as **N alternatives the
player picks among** than a single forced action.

### 3.1 Shape

`AdviceItem` gains one optional field:

| field | type | notes |
|---|---|---|
| `options` | `AdviceOption[]?` | **null** for single-option advice (e.g. a power deficit). Non-null ⇒ multi-option advice; the dashboard renders a picker. |

`AdviceOption`:

| field | type | notes |
|---|---|---|
| `id` | `string` | stable within the advice item; echoed back on pick + in apply/feedback logs |
| `label` | `string` | short picker label ("Compact 5×5 freezer") |
| `summary` | `string` | one-line tradeoff-free description |
| `blueprint_group` | `BlueprintGroup` | the placement payload this option would Apply (see §4) |
| `est_materials` | `MaterialEstimate[]` | `{def_name, count}` per material — aggregated from the group's per-asset costs (RIMAPI `validate` returns per-asset `cost[]`, see `rimapi…` §3.1) |
| `tradeoff_note` | `string?` | optional ("cheaper, but no expansion room") |

`MaterialEstimate` = `{ def_name: string, count: int }` — deliberately the same
`{def_name,count}` shape RIMAPI's `validate.cost[]` and `PendingBuildDto` use,
so the dashboard can reconcile estimate vs. live cost without translation.

### 3.2 Single- vs multi-option serialization

- **Single-option advice** (power deficit, material bottleneck): `options` is
  omitted/null. The actionable operation lives in `actions[]` as today (e.g. a
  `place_blueprint` action whose `apply` is a one-asset `place_blueprint_group`,
  or a non-apply `note`/`set_priority`). No picker.
- **Multi-option advice** (freezer/room layout): `options[]` is populated.
  `actions[]` still carries the non-placement guidance (e.g. "free up steel
  first", a `note`, a `set_priority`); the *placement choice itself* is the
  `options[]` picker rather than N parallel `place_blueprint` actions, so the
  dashboard does not show four competing Apply buttons.

> **Invariant:** an option's `blueprint_group` is the **only** way that option
> becomes executable. The LLM proposes options as data; deterministic code runs
> each group through the §2.3 eligibility gate. An option that fails validation
> renders as a (greyed) suggestion, never an Apply button.

### 3.3 How a dashboard pick maps to an Apply

1. Minister emits an `AdviceItem` with `options[]` (each option pre-carrying its
   `blueprint_group`).
2. Player picks option `id = X` in the dashboard.
3. Dashboard sends the pick to the Host (advice id + option id) — **it does not
   send a constructed payload**; the server already holds option X's
   `blueprint_group` (LLM-never-picks-payload rule preserved).
4. Host wraps option X's `blueprint_group` as a `place_blueprint_group` apply
   (the §2.1 envelope + §4 payload), runs the §2.3 eligibility/validate gate
   against fresh state, and **only then** lights the Apply button.
5. Player clicks Apply → Host loops the group's ordered placements through the
   RIMAPI validate→place→read triplet (`rimapi…` §3), logging per `advice.md`
   "Apply attempts must be logged…", and on `applied`/`already_satisfied`
   suppresses the action per `advice.md`.

The consent boundary stays the **Apply click** (step 5); the pick (step 2) only
selects which group is the candidate.

---

## 4. `blueprint_group` shape

**RimWorld fact applied:** both **rooms** (walls + door + floor) and the
**buildings inside them** (cooler, shelf, bench) are placed via blueprints. A
`blueprint_group` is therefore an **ordered** list of single-asset blueprint
placements covering a room shell **plus** its contents, placeable as one unit.

Field names are aligned to RIMAPI's single-asset DTOs
(`BlueprintValidateRequestDto` / `BlueprintPlaceRequestDto`,
`rimapi-blueprint-placement-endpoint.md` §3.1–3.2): `def_name`,
`stuff_def_name?`, `cell {x,z}`, `rotation` (int 0–3, `Rot4.AsInt`).

```jsonc
// BlueprintGroup
{
  "label": "Compact 5x5 freezer",   // group display name (option/apply label source)
  "map_id": 1,                       // echoes the apply-envelope map_id
  "assets": [                        // ORDERED: shell first, then contents
    {
      "role": "floor",              // "wall" | "floor" | "door" | "building" — render/order hint, not an exec input
      "def_name": "Floor_Wood",     // ThingDef OR TerrainDef defName (RIMAPI resolves either)
      "stuff_def_name": null,        // null for stuffless defs and terrain
      "cell": { "x": 12, "z": 34 }, // absolute map cell — matches RIMAPI {x,z}; NOT MapPosition's {x,y,z}
      "rotation": 0                  // 0-3
    },
    { "role": "wall",  "def_name": "Wall",   "stuff_def_name": "BlocksGranite", "cell": {"x":12,"z":34}, "rotation": 0 },
    { "role": "door",  "def_name": "Door",   "stuff_def_name": "WoodLog",       "cell": {"x":14,"z":34}, "rotation": 0 },
    { "role": "building", "def_name": "Cooler", "stuff_def_name": "Steel",      "cell": {"x":13,"z":36}, "rotation": 2 }
  ]
}
```

### Design points / alignments

- **Per-asset, not bulk.** Each `assets[]` entry is a 1:1 match for RIMAPI's
  per-asset validate/place DTO. This deliberately avoids RIMAPI's legacy bulk
  `BlueprintDto` (which couples floors+buildings and silently skips — flagged as
  tech debt in `rimapi…` §4). Apply loops single-asset calls.
- **`cell` is `{x,z}`, not `MapPosition`.** RimBob's `MapPosition` is `{x,y,z}`;
  the RIMAPI blueprint cell is `{x,z}` (`y`/altitude is implicit, RIMAPI builds
  `new IntVec3(cell.x, 0, cell.z)`). The group uses RIMAPI's 2-axis cell to
  avoid a translation layer at the apply boundary. **Open question Q3** asks
  whether to introduce a shared `MapCell {x,z}` record so this is typed, not a
  raw object.
- **Ordering is load-bearing.** Floor → walls → door → interior buildings so
  per-asset placement can't be rejected for "no floor"/"inside a wall" ordering
  reasons. `role` is the ordering/render hint; it is **not** sent to the RIMAPI
  (the RIMAPI only needs def/stuff/cell/rotation).
- **No rect at group level.** Walls are enumerated as individual wall-segment
  cells (RimWorld walls are per-cell things), not a filled rectangle, because
  the RIMAPI places one thing per cell and a rect would imply area-paste (which
  the RIMAPI plan §1/§8 explicitly excludes). Floors that genuinely tile a region
  are still enumerated per-cell in `assets[]`. **Open question Q2** asks whether
  large floor fills need a compressed rect-of-floor representation to keep
  payloads small.
- **Cost.** No cost is stored *in* the group; it is derived per-asset from the
  RIMAPI `validate` `cost[]` and aggregated into the option's `est_materials`
  (§3.1). Single source of truth for materials = the RIMAPI.

### Limits (new, for the blueprint/group kind)

`AssistedApplyLimits` should gain a `MaxBlueprintGroupAssets` bound (a freezer
shell + cooler is well under it; the point is to refuse pathological
LLM-emitted mega-groups). A concrete number is **Open question Q1**. The same
`MaxMissingTargetFraction`-style fresh-state revalidation principle applies: if
re-validation finds any asset newly un-placeable, the whole group Apply should
fail closed (atomic-ish), or fall to a documented partial-place policy — also
Q1.

---

## 5. Which Willie concerns carry `options[]`

From [`willie-advice-types.md`](willie-advice-types.md) (9 canonical concerns). Split
by whether the advice is "build/move *this* specific thing" (single action) vs.
"here are real spatial alternatives" (multi-option picker):

| concern | options[]? | Why |
|---|---|---|
| `thermal_control` | **Yes** (typical) | Freezer placement has genuine alternatives (size, single vs. double-cooler, location vs. kitchen) — the canonical multi-option case. |
| `functional_rooms` | **Yes** (typical) | "Where/how big a hospital/workshop" has competing footprints. (LLM-tier per [`willie-advice-types.md`](willie-advice-types.md) §5.) |
| `base_layout` | **Yes** (often) | Whole-base topology / route-chain fixes have competing rearrangements ("competing expansion directions / flat-base trade-offs", [`willie-advice-types.md`](willie-advice-types.md) §2/§5). |
| `storage_placement` | **Sometimes** | A shelf can have 2–3 candidate spots near the consuming bench; a single obvious spot is single-action. |
| `basic_shelter` | **Sometimes** | Usually a single "add N beds / enclose this" action; could offer barracks-vs-cells when escalated. |
| `fire_risk` | **Mostly single** | "Rebuild this wall in stone" / "add a firebreak gap" is a targeted single action; a spacing fix may rarely offer 2 options. |
| `stalled_builds` | **Single** | The fix is "supply material X" / "clear the unreachable tile" — a request/note, not a placement choice. |
| `material_bottleneck` | **Single** | Resolution is a `RequestResource` flag / `note` (request side, other agent); no placement options. |
| `power_stability` | **Single** | The canonical *non*-multi-option example in the brief: deficit → one corrective action (battery/genny placement is usually one obvious add or a request). |

Rule of thumb: **types that emit a room-shell-or-contents `blueprint_group`
with real spatial freedom carry `options[]`; types whose fix is a request, a
knob, a note, or one obvious placement do not.** This also tracks the Slice-A
(rules-first, single-action) vs. Slice-C (LLM, judgment, multi-option) split in
[`willie-advice-types.md`](willie-advice-types.md) §5 — the LLM tiers are exactly the ones that produce alternatives.

---

## 6. Open questions

- [ ] **Q1 — Group size + atomicity limits.** What is `MaxBlueprintGroupAssets`?
      And on partial fresh-state revalidation failure mid-group, is Apply
      all-or-nothing, or place-valid-and-report-skipped (the RIMAPI can't truly
      transact across per-asset calls — `rimapi…` §6 lands writes at frame
      cadence)? Needs the human's blast-radius call.
- [ ] **Q2 — Floor-fill representation.** Enumerating every floor cell in
      `assets[]` bloats a large room's payload. Do we add a compressed
      "fill this rect with terrain X" group-asset variant (expanded to per-cell
      RIMAPI calls server-side), or cap room size to keep enumeration small?
      Touches the RIMAPI-plan "no area paste" boundary, so coordinate with the
      RIMAPI slice.
- [ ] **Q3 — Typed cell.** Should `cell {x,z}` be a shared `MapCell` record
      (distinct from `MapPosition {x,y,z}`) so the blueprint payload is typed,
      or stay a raw 2-field object matching the RIMAPI wire? Affects both repos'
      DTO mirrors.
- [ ] **Q4 — Discriminated-union encoding in C#.** Confirm the per-kind apply
      split is a sealed `AdviceApplyPayload` hierarchy + `System.Text.Json`
      polymorphic `[JsonDerivedType]` discriminator vs. keeping a single record
      with a validated subset. (Source-of-record decision; flagged so the
      implementer doesn't silently reconstruct the 11-nullable bag.)
- [ ] **Q5 — Pick persistence + supersession.** When a multi-option card is
      superseded by a fresh minister snapshot, does an already-picked-but-not-
      yet-applied option survive, or does the player re-pick? Interacts with
      `advice.md` "Expiry And Supersession" and the latest-only snapshot store.
- [ ] **Q6 — Feedback granularity.** Does picking option X (then Accept/Dismiss)
      record *which* option was chosen in the feedback/replay record? High-value
      refinement signal ("players always pick the compact freezer"), but it
      extends the feedback shape owned partly by `advice.md` lifecycle — confirm
      with the feedback design.
- [ ] **Q7 — Request-side boundary.** `material_bottleneck` /
      `stalled_builds` resolve via a resource/flag request, not a
      placement. Confirm with `.plans/willie-request-taxonomy.md` (not yet
      written) that those land as `ResourceRequest`/`AgentFlag` and that no
      output-side field duplicates them.

---

## 7. Authorship & dashboard ownership (added after review)

Two clarifications that change *who writes what* and *where the player acts*.
They do **not** change the wire shapes in §2–§4.

### 7.1 Two-layer authorship — the LLM does NOT emit `blueprint_group`

Exact cells/rotations are **not** authored by the minister LLM. A simple LLM
cannot reliably emit per-cell placement, and `advice.md` already forbids the LLM
choosing payloads/target ids. Split authorship:

- **Judgment layer (LLM or rules):** emits a small semantic `ConstructionIntent`
  — `concern`, `priority`, title/body/rationale, and an optional
  `build_intent { target_class, room_class?, size_hint?, capacity?, near?[],
  constraints?[] }`. **No coordinates.**
- **Placement layer (the deterministic [Placement Solver](placement-solver.md)):**
  reads the live map, generates 1–3 candidate `blueprint_group`s satisfying the intent,
  RIMAPI-`validate`s each, computes `est_materials`, and assembles `options[]` +
  the `apply` payloads.

So `options[]` / `blueprint_group` / apply payloads in §2–§4 are the
**stored/wire shape produced by code**, attached to the advice *after* the LLM
returns its intent. Implication: the **Placement Solver** ([plan](placement-solver.md))
is a **new deterministic component** (maps to HumanTodo `construction-placement-layout-strategy-base`);
early Willie slices emit prose advice + `build_intent` with `options = null` until
that engine + the RIMAPI blueprint-group endpoints exist.

### 7.2 The Apply (and the layout suggestions) live in the **Willie tab**

Food requests "a freezer" (a vague `building_request` on a flag — no layout, no
button). **Willie parses all inbound build requests, and per request emits one
advice card with ~3 layout `options[]` + the pick/Apply — all in Willie's own
dashboard scope.** A requesting minister's tab shows only its *outbound* request
(informational); it never carries a `place_blueprint` Apply, because it never
emits that action. The build/placement is Willie's domain, so the executable
surface is Willie's tab.

Optional: cross-link the Food request ↔ the Willie advice for traceability
(advice-chain-visualization), which is a render-time link, not a button move.
This is also a [`dashboard.md`](../Docs/design/dashboard.md) contract point
("Apply controls render in the owning minister's scope") to fold in at promotion.
