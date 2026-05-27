# RIMAPI Reference (cached summary)

Cached digest of the upstream [RIMAPI](https://github.com/IlyaChichkov/RIMAPI) docs at [api.html](https://ilyachichkov.github.io/RIMAPI/api.html). The full reference is large (167 endpoints, v1.9.0); this file exists so future agents can answer "is there an endpoint for X?" without re-fetching the live docs.

RimBob's active runtime mod is the local fork at `C:\dev\RIMAPI-for-RimBob`
(repo `ocaspi-sc/RIMAPI-for-RimBob`), installed into RimWorld as
`RIMAPI-for-RimBob` with package id `ocaspi.rimapi.rimbob`. Endpoint changes
needed by RimBob should land in that sibling fork, not in this repo and not in
the upstream `IlyaChichkov/RIMAPI` checkout.

The RimBob fork targets RimWorld 1.6 only. Build and install fork changes with
`Release-1.6`; do not add `Release-1.5`, `RIMWORLD_1_5`, or other old-game
compatibility paths.

If you need an endpoint not listed here, fetch the live docs and append to this file.

> **Verified vs. cached.** The catalogue below was distilled from upstream docs and is **not** all field-checked against the running mod. Verified shapes are called out inline as slices wire them into ingestion. Several DTO field names that previously diverged from the live API have been corrected against the running RIMAPI (e.g. `tick` → `game_tick`, `wealth` → `colony_wealth`, `paused` → `is_paused`, `mapId` → `map_id`); other endpoint DTOs remain speculative until a minister actually wires them.

---

## Globals

- **Base URL:** `http://localhost:8765/api/v1` (port configurable in mod settings)
- **Pawn controller v2 prefix:** `/api/v2`
- **Auth:** none
- **Convention:** snake_case for all JSON keys (request and response)
- **Envelope:** `{ "success": bool, "data": {...}, "errors": [], "warnings": [], "timestamp": "ISO-8601" }`
- **Coords:** `{ "x", "y", "z" }`
- **Status codes:** 200 / 400 / 404 / 500
- **SSE:** the `/stream/*` endpoints under Camera Controller stream the **camera video feed**, not game events. There are no event SSE endpoints — poll for game events. (See [state-store.md](state-store.md).)
- **Discovery:** `GET /api/v1/dev/endpoints` lists every endpoint. The
  unprefixed `/dev/endpoints` and `/docs` routes returned 404 in the live
  RimBob fork smoke check.

### Collection DTO rule

RIMAPI collection endpoints are not perfectly uniform. Some return `data: []`;
some return `data: {}` for an empty collection; some wrap the useful array in
a named property such as `data.zones` or `data.incidents`. RimBob should parse
known wrappers explicitly. Unknown non-empty objects and malformed array items
are schema drift and must fail loudly; they must not be converted to empty
lists.

DTO id fields are not fully consistent across endpoints. Verified examples include numeric animal ids from `/api/v1/map/animals?map_id=...`; ingestion keeps aggregate ids as strings, so unverified string-like DTO ids use a flexible string-id converter that accepts JSON strings or numbers.

Live local contract tests under `Src/Tests/Live` are marked `Category=Live`. They are GET-only, early-return when RIMAPI/Host is not reachable, and act as local tripwires when RimWorld or RimBob Host is running.

---

## Endpoint catalogue

Categories below are exhaustive at the controller level (167 endpoints total). Within each controller only the endpoints we have confirmed are listed; controllers marked **(details not cached)** are known to exist but their endpoint shapes haven't been digested yet — fetch live docs before using.

### Game
| Method | Path | Purpose |
|---|---|---|
| GET | `/game/state` | tick, wealth, colonist count, storyteller, paused |
| POST | `/game/speed?speed=0..4` | set tick speed |
| GET | `/version` | rim/mod/api versions |
| GET | `/mods/info` | active mods |
| POST | `/mods/configure` | reorder/restart mods |
| GET | `/datetime` / `/datetime/tile` | in-game date |
| GET | `/def/all` | all defs (things, incidents, traits, …) |
| POST | `/select` / `/deselect` / `/open-tab` | UI selection |
| POST | `/game/select-area` | rect select |
| POST | `/game/send/letter` | push notification letter |
| POST | `/game/save?name` / `/game/load?name` | save/load |
| POST | `/game/start` / `/game/start/devquick` | new game |
| GET | `/game/settings` + `/game/settings/run-in-background` (+ toggle) | settings |
| POST | `/game/main-menu` / `/game/quit` | exit |

> **Verified shape.** `/def/all` returns a non-empty object under `data`, with thing defs nested at `data.things_defs`. Food uses this catalog to read item nutrition; live `MealSurvivalPack` has `nutrition: 0.9`, `stack_limit: 10`, and item/category metadata. The RimBob fork also exposes `animal_defs` so Food can score hunt risk/value from compact animal metadata instead of relying only on def-name deny lists.

### Game Events (incidents, quests, lords)
| Method | Path | Purpose |
|---|---|---|
| GET | `/quests?map_id` | active + historical quests |
| GET | `/incidents?map_id` | recent incidents w/ days_since |
| GET | `/incidents/top?limit` | weighted incident table |
| GET | `/incident/chance?incident_def_name` | probability |
| POST | `/incident/trigger` | force incident |
| GET | `/lords?map_id` | AI lords (raids, caravans) |

### Faction
| Method | Path | Purpose |
|---|---|---|
| GET | `/factions` | list w/ goodwill, relation |
| GET | `/faction?id` / `/faction/def?name` / `/faction/icon?id` | one faction |
| GET | `/faction/player` | player faction |
| GET | `/faction/relations?id` / `/faction/relation-with?id&other_id` | bilateral |
| POST | `/faction/change/goodwill` (delta) / `/faction/goodwill` (set) | adjust |

### Map (per-map data)
| Method | Path | Purpose |
|---|---|---|
| GET | `/maps` | list maps |
| GET | `/map/things?map_id` | every thing |
| GET | `/map/things-at` (body position) | things at cell |
| GET | `/map/things/radius?map_id&x&z&radius` | things in radius |
| GET | `/map/pawns?map_id` | pawns w/ name, health, mood, hunger, position |
| GET | `/map/terrain?map_id` | RLE terrain grid |
| GET | `/map/fog-grid?map_id` | RLE visibility |
| GET | `/map/reach?map_id&from_x&from_z&to_x&to_z` | fork-only cell reachability wrapper |
| POST | `/map/path-cost` | fork-only single-pair region/A* path cost |
| POST | `/map/path-cost/batch` | fork-only bounded batch region/A* path costs |
| GET | `/map/ore?map_id` | ore deposits |
| GET | `/map/plants?map_id` | plants w/ growth |
| GET | `/map/animals?map_id` | wild + tame |
| GET | `/map/zones?map_id` | zones + areas |
| GET | `/map/rooms?map_id` | rooms w/ role, temp, beds, roof/open signals, quality stats |
| GET | `/map/buildings?map_id` | all buildings |
| GET | `/map/building/info?id` | one building |
| GET | `/map/weather?map_id` | weather + temp |
| POST | `/map/weather/change?map_id&name` | force weather |
| GET | `/map/power/info?map_id` | power grid totals |
| GET | `/map/creatures/summary?map_id` | counts (colonists, enemies, animals) |
| GET | `/map/farm/summary?map_id` | crop totals + avg growth |
| POST | `/map/zone/growing` | create grow zone (def + rect) |
| POST | `/map/zone/stockpile` / `…/update` / DELETE `…/delete` | stockpile CRUD |
| POST | `/map/destroy/corpses` / `/map/destroy/forbidden` / `/map/destroy/rect` | cleanup |
| POST | `/map/repair/positions` / `/map/repair/rect` | repair |
| POST | `/map/droppod` | spawn drop pod |
| POST | `/map/building/power?buildingId&powerOn` | toggle power |

> **Verified shape.** `/map/things?map_id=...` returns broad map items including `thing_id`, `def_name`, `label`, `categories`, `position`, `stack_count`, `market_value`, and `is_forbidden`. It includes forbidden map items, so Food treats it as fallback/debug inventory; `/resources/stored` is preferred for reachable stored food.
>
> **Verified shape.** `/map/terrain?map_id=...` returns map dimensions, a terrain palette, and an RLE grid. `/def/all` terrain definitions include fertility and affordances; Food uses those as compact crop-fertility context, not as an exact placement solver.

> **Fork shape.** `/map/reach`, `/map/path-cost`, and
> `/map/path-cost/batch` expose default in-map reachability and walk-cost
> scoring for Willie placement ranking. `reach` wraps
> `Map.reachability.CanReach`; `path-cost` uses `tier:"region"` for cheap
> region-BFS rank or `tier:"astar"` for exact RimWorld pathfinder cost; batch
> requests are capped at 4096 pairs and reject the whole batch on malformed
> cells. These endpoints are read-only and not pawn-specific.

> **RimBob wrapper coverage.** `RimApiClient` now has typed wrappers for all
> three endpoints. They are network primitives only until Placement Solver PS1
> consumes them.

> **Verified shape.** `/map/farm/summary?map_id=...` returns live growing-zone crop rows under `data.crop_types[]`, not the cached `crop_breakdown[]` shape. Useful fields include `total_plants`, `growth_progress_average` as a percent value, and per-crop `plant_def_name`, `total_plants`, `harvestable_plants`, and numeric `zone_id`. Food uses this as the primary crop count/growth/zone source.

> **Verified shape.** `/map/plants?map_id=...` currently returns broad thing-like plant rows with `thing_id`, `def_name`, `label`, `categories`, `position`, `stack_count`, and `is_forbidden`. It may not include growth, crop, or zone fields, so Food should not rely on this endpoint alone to know which crop is growing; combine it with `/map/farm/summary`.

> **Verified historical shape.** `/map/animals?map_id=...` could omit health/tame fields on ordinary wild animals. Missing health meant "not reported", not injured/dead; ingestion defaulted it to healthy for Chef's wild-animal opportunity count. The RimBob fork now emits `tame` and `health` where RimWorld exposes them, which lets Chef exclude tame or unhealthy animals before hunt scoring.

> **Verified shape (RimBob fork).** `/map/rooms?map_id=...` returns `data.rooms[]`. Room rows include `id`, `role_label`, `temperature`, `cells_count`, `touches_map_edge`, `is_prison_cell`, `is_doorway`, `open_roof_count`, `contained_beds_ids[]`, and room stats `impressiveness`, `beauty`, `cleanliness`, `space`, `wealth`. RimBob ingests this into `RoomRegistry` for read-only Welfare source briefings and Willie/Welfare evidence.

### Bill (work-table recipes)
| Method | Path | Purpose |
|---|---|---|
| GET | `/map/work-tables?map_id` | list tables |
| GET | `/buildings/recipes?building_id` | available recipes; wrapped for simple-meal apply |
| GET | `/buildings/bills?building_id` | bills on a table; ingested for Food bill-state awareness and wrapped for simple-meal apply |
| POST | `/buildings/bills/add` | add bill; wrapped only for simple-meal apply |
| DELETE | `/buildings/bills/remove` | clear all |
| GET | `/buildings/bill?building_id&bill_id` | one bill |
| PUT | `/buildings/bill/update` | edit; wrapped only for simple-meal target update |
| DELETE | `/buildings/bill/remove` | delete one |
| PUT | `/buildings/bill/reorder` (offset) | priority |
| PUT | `/buildings/bill/suspend` | pause |

> **Verified shape.** The RimBob fork registers these bill endpoints in
> `/api/v1/dev/endpoints`. Recipe rows include `def_name`, `label`,
> `work_amount`, `work_skill`, `products[]`, and `ingredients[]`. Bill rows
> include `load_id`, `recipe_def_name`, `recipe_label`, `repeat_mode`, and
> `target_count`. RimBob ingests the compact current bill state for cooking
> workbenches so Food can avoid stale bill suggestions. Create bill accepts
> `recipe_def_name`, `repeat_mode`, and `target_count`; update bill accepts
> `repeat_mode` and `target_count`. RimBob wraps only the idempotent
> simple-meal `TargetCount` path.

### Builder (blueprints)
| Method | Path | Purpose |
|---|---|---|
| POST | `/builder/copy` (rect) | copy area |
| POST | `/builder/paste` | paste blueprint |
| POST | `/builder/blueprint` | legacy copied-area blueprint placement |
| POST | `/builder/blueprint/validate` | fork-only dry-run for one caller-supplied blueprint |
| POST | `/builder/blueprint/place` | fork-only safe placement for one validated blueprint |
| POST | `/builder/blueprint/allowed-state` | fork-only allow/disallow for explicit pending blueprint/frame ids |
| POST | `/builder/blueprint/cancel` | fork-only cancel for explicit pending blueprint/frame ids |
| GET | `/map/blueprints?map_id` | fork-only pending `Blueprint_Build` and `Frame` read |
| GET | `/map/construction/backlog?map_id` | fork-only grouped pending blueprint/frame backlog |

> **Fork lifecycle shape.** The RimBob fork blueprint slice is strictly about
> pending blueprint/frame lifecycle: validate, place, read, allow/disallow,
> explicit-id cancel, and backlog summary. It does not cover general building
> detail, room, stockpile, power-net, or buildability-layer evidence; those are
> separate Willie follow-ups in `HumanTodo.md`.

> **RimBob consumption.** The state store now consumes
> `/map/construction/backlog` into the `WillieBacklog` aggregate and derives
> material-bottleneck / stalled-builds summaries for `WillieBriefing`.

### Order (designations)
| Method | Path | Purpose |
|---|---|---|
| POST | `/order/designate/area` | designate Mine / Deconstruct / Harvest / Hunt over a rect |
| POST | `/order/unforbid` | safely clear the forbidden flag on explicit haulable thing ids |

> **Verified shape (RimBob fork).** `/order/unforbid` accepts `map_id` and
> `thing_ids[]`. The sibling fork rejects empty or oversized batches, malformed
> ids, missing targets, non-map targets, and non-haulable targets. Successful
> responses include `requested`, `matched`, `changed`, `already_allowed`,
> `missing`, `non_map_targets`, and `non_item_targets`.

### Lord (AI groups)
| Method | Path | Purpose |
|---|---|---|
| POST | `/lords/create` | spawn lord with faction + pawn_ids + job_type |

### Global Map (world)
| Method | Path | Purpose |
|---|---|---|
| GET | `/world/caravans` | caravans w/ tile, pawns, items |
| GET | `/world/caravan/path?caravan_id` | route + progress |
| GET | `/world/settlements` / `/world/player/settlements` | settlements |
| GET | `/world/sites` | temporary sites |
| GET | `/world/tile?id` / `/world/tile/details?id` / `/world/tile/coordinates?id` | tile info |
| GET | `/world/grid` / `/world/grid/area?tile_id&radius` | tiles bulk |

### Camera & Stream (video)
| Method | Path | Purpose |
|---|---|---|
| POST | `/camera/change/zoom` / `/camera/change/position` | move camera |
| POST | `/stream/start` / `/stream/stop` / `/stream/setup` | JPEG video stream |
| GET | `/stream/status` | streaming flag + config |

### Image
| Method | Path | Purpose |
|---|---|---|
| GET | `/terrain/image?name` | base64 PNG for a TerrainDef |
| GET | `/item/image?name` | base64 PNG for a ThingDef/item/building/plant/apparel/weapon-like def |
| GET | `/faction/icon?id` | base64 PNG plus color metadata for a current-world faction |
| GET | `/pawn/portrait/image?pawn_id&width&height&direction` | base64 pawn portrait PNG |
| GET | `/colonist/body/image?id` | base64 body/head PNGs plus color metadata |
| POST | `/item/change/image` | upload custom texture |

RimBob exposes only read-only image gateways through Host. `/item/change/image`
is intentionally not wrapped or exposed because it mutates game textures and is
outside the localhost read-only dashboard posture.

The static cache warmer uses `/def/all`, `/item/image`, `/terrain/image`,
`/factions`, and `/faction/icon` to cache enumerable static art locally under
the stable machine-local RimBob icon cache root. Pawn portraits, colonist body
images, stuff-colored variants, growth-stage variants, styled variants,
rotations, projectiles, motes, and other per-instance hard cases are skipped or
fetched lazily. `RimBob:IconCacheRoot` may override that cache root; relative
overrides resolve under the same stable machine-local RimBob root.

### Overlay / UI
| Method | Path | Purpose |
|---|---|---|
| POST | `/ui/announce` | on-screen announcement |

### Pawn (v2) — partial
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v2/colonists/detailed?map_id` | full bio + needs + skills + health per colonist |

> **Verified shape (M1.5+).** `/api/v2/colonists/detailed` returns a list of objects with two fields: `pawn` (id, name, gender, age, health, mood, hunger, position) and `detailes` (yes, that spelling - `work_info.{skills, current_job, traits}`, `medical_info.{is_dead, is_downed, hediffs[]}`, `social_info`, `policies_info`). The RimBob fork also exposes wellbeing source fields under `detailes`: `sleep`, `comfort`, `beauty`, `joy`, `fresh_air`, `drugs_desire`, and `mood_thoughts[]` with `def_name`, `label`, `mood_offset`, `stage_index`. `skill.passion` is an int (0=None, 1=Minor, 2=Major), `skill.name` (not `def`) holds the skill key. Trait entries are objects (`{name, label}`), not bare strings.

### Resources (Thing controller)
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/resources/summary?map_id` | colony-wide rollup: total items, market value, food/medicine/weapons rollups |
| GET | `/api/v1/resources/stored?map_id` | stored item stacks grouped by resource category |
| GET | `/api/v1/resources/storages/summary?map_id` | stockpile cell utilization (`total_stockpiles`, `used_cells`, `utilization_percent`) |

> **Verified shape.** `/resources/summary.critical_resources` carries `food_summary.{food_total, total_nutrition, meals_count, raw_food_count}`, `medicine_total`, `weapon_count`, `weapon_value`. `total_nutrition`, `meals_count`, and `raw_food_count` can be stale or under-classified even when stored food exists, so item/def-backed classification wins when available.

> **Verified shape.** `/resources/stored?map_id=...` returns `data` as a category object, not a per-def dictionary. Examples include `food_meals: [ThingDto...]` and `plant_food_raw: [ThingDto...]`. A live stockpile with packaged survival meals surfaced four non-forbidden `MealSurvivalPack` stacks totaling 32 meals; the same map also had forbidden meal stacks visible through `/map/things`, which is why stored resources are the primary food-count source.

### Research
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/research/progress` | current project: `name`, `label`, `progress`, `progress_percent`, `is_finished`, `can_start_now` |
| GET | `/api/v1/research/finished` | list of completed projects |
| GET | `/api/v1/research/tree` | full tech tree |
| GET | `/api/v1/research/project?name` | one project's metadata + prerequisites |
| GET | `/api/v1/research/summary` | by-tech-level rollups (finished / total / percent) |
| POST | `/api/v1/research/target` | set current project by defName |
| POST | `/api/v1/research/stop` | stop current project |

> **Verified shape (M1.5).** `/research/progress` returns sentinel `{name:"none", label:"None", progress_percent:0}` when nothing is selected; the dispatcher maps that to `ResearchInfo.CurrentProject = null`.

### DevTools
| Method | Path | Purpose |
|---|---|---|
| GET | `/dev/endpoints` (full path `/api/v1/dev/endpoints`) | discoverable list of every endpoint |
| GET | `/dev/materials-atlas` / POST `/dev/materials-atlas/clear` | atlas |
| POST | `/dev/console` | run dev console action |
| POST | `/dev/stuff/color` | recolor stuff def |

### Documentation
| Method | Path | Purpose |
|---|---|---|
| GET | `/docs` / `/docs/health` / `/docs/export` / `/core/docs/export` | API docs |
| GET | `/docs/extensions/{extensionId}` | extension docs |

---

## Controllers known to exist but NOT yet cached

Live docs name these controllers; their endpoint shapes are not in this file. **Fetch live docs before relying on them:**

- **Pawn Info Controller** — read pawn fields not covered by `/api/v2/colonists/detailed`
- **Pawn Edit Controller** — likely target for work priorities, schedules, drug/food policies, apparel/equipment changes. Split by direction: the near-term need is a **read** of current work priorities/policies so ministers can emit accurate Suggest-mode `set_priority` advice (a minister cannot tell if a knob is already set without it). The matching **writes** are deferred Auto-epic policy knobs Labor recommends — see [`DESIGN.md`](DESIGN.md) decision "Auto execution delegates to the game's native automation," [ministers/labor.md](ministers/labor.md), [ministers/welfare.md](ministers/welfare.md). Fetch and cache the GET shape here when the first `set_priority`-emitting minister wires it.
- **Pawn Job Controller** — direct job/forced-work assignment
- **Pawn Spawn Controller** — create/remove pawns

When a minister needs an endpoint from one of these, fetch the upstream docs, add the rows to this file, then implement in [RimApiClient.cs](../Ingestion/RimApiClient.cs).

---

## How RimBob uses RIMAPI

- Only [`RimApiClient`](../Ingestion/RimApiClient.cs) calls RIMAPI. Ministers read from the [state store](state-store.md), never RIMAPI directly.
- Add a method to `RimApiClient` only when a minister actually needs it — no speculative coverage.
- Polling cadences are defined in [state-store.md](state-store.md) (slow / fast / event-diff). No SSE consumption.
- Write ownership per minister is defined in `design/ministers/<name>.md`. Only Labor issues pawn-allocation writes ([labor.md](ministers/labor.md)). MVP Assisted Apply may use a tiny non-pawn write allowlist after player confirmation; fetch and document upstream endpoint shapes before adding each write.
- Upstream is GPL-3.0; we link only via HTTP, never in-process.

## RimBob Host Output Endpoints

The Host-facing dashboard contract for player-facing minister output is now the
uniform minister snapshot surface, not an Agenda-specific route family:

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/ministers/{minister}/snapshot` | Latest typed minister output snapshot. `mayor` returns `MayorAgenda`; feeders return `AdviceSnapshot`. |
| POST | `/api/ministers/mayor/snapshot/manual` | Developer manual Mayor snapshot fallback ingestion. |
| GET | `/api/advice/stream` | SSE replay/live feed for Mayor agenda updates and feeder advice snapshots. |

`/api/agenda/latest`, `/api/agenda/history`, and `/api/agenda/manual` are
retired with no compatibility aliases. Status payloads use
`mayor_snapshot_version` instead of `agenda_version`; SYSTEM exposes the
resolved minister output root and per-minister persistence metadata.
