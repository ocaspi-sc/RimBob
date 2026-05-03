# RIMAPI Reference (cached summary)

Cached digest of the upstream [RIMAPI](https://github.com/IlyaChichkov/RIMAPI) docs at [api.html](https://ilyachichkov.github.io/RIMAPI/api.html). The full reference is large (167 endpoints, v1.9.0); this file exists so future agents can answer "is there an endpoint for X?" without re-fetching the live docs.

If you need an endpoint not listed here, fetch the live docs and append to this file.

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
- **Discovery:** `GET /dev/endpoints` lists every endpoint; `GET /docs` returns docs.

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
| GET | `/map/pawns?mapId` | pawns w/ name, health, mood, hunger, position |
| GET | `/map/terrain?map_id` | RLE terrain grid |
| GET | `/map/fog-grid?map_id` | RLE visibility |
| GET | `/map/ore?map_id` | ore deposits |
| GET | `/map/plants?map_id` | plants w/ growth |
| GET | `/map/animals?map_id` | wild + tame |
| GET | `/map/zones?map_id` | zones + areas |
| GET | `/map/rooms?map_id` | rooms w/ role, temp, beds |
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

### Bill (work-table recipes)
| Method | Path | Purpose |
|---|---|---|
| GET | `/map/work-tables?map_id` | list tables |
| GET | `/buildings/recipes?building_id` | available recipes |
| GET | `/buildings/bills?building_id` | bills on a table |
| POST | `/buildings/bills/add` | add bill |
| DELETE | `/buildings/bills/remove` | clear all |
| GET | `/buildings/bill?building_id&bill_id` | one bill |
| PUT | `/buildings/bill/update` | edit |
| DELETE | `/buildings/bill/remove` | delete one |
| PUT | `/buildings/bill/reorder` (offset) | priority |
| PUT | `/buildings/bill/suspend` | pause |

### Builder (blueprints)
| Method | Path | Purpose |
|---|---|---|
| POST | `/builder/copy` (rect) | copy area |
| POST | `/builder/paste` | paste blueprint |
| POST | `/builder/blueprint` | place blueprint at position |

### Order (designations)
| Method | Path | Purpose |
|---|---|---|
| POST | `/order/designate/area` | designate Mine / Deconstruct / Harvest / Hunt over a rect |

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
| GET | `/terrain/image?name` / `/item/image?name` | base64 texture |
| POST | `/item/change/image` | upload custom texture |

### Overlay / UI
| Method | Path | Purpose |
|---|---|---|
| POST | `/ui/announce` | on-screen announcement |

### Pawn (v2) — partial
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v2/colonists/detailed?map_id` | full bio + needs + skills + health per colonist |

### DevTools
| Method | Path | Purpose |
|---|---|---|
| GET | `/dev/endpoints` | discoverable list of every endpoint |
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
- **Pawn Edit Controller** — likely target for work priorities, schedules, drug/food policies, apparel/equipment changes (writes Labor/Welfare own — see [ministers/labor.md](ministers/labor.md), [ministers/welfare.md](ministers/welfare.md))
- **Pawn Job Controller** — direct job/forced-work assignment
- **Pawn Spawn Controller** — create/remove pawns

When a minister needs an endpoint from one of these, fetch the upstream docs, add the rows to this file, then implement in [RimApiClient.cs](../Ingestion/RimApiClient.cs).

---

## How RimAI uses RIMAPI

- Only [`RimApiClient`](../Ingestion/RimApiClient.cs) calls RIMAPI. Ministers read from the [state store](state-store.md), never RIMAPI directly.
- Add a method to `RimApiClient` only when a minister actually needs it — no speculative coverage.
- Polling cadences are defined in [state-store.md](state-store.md) (slow / fast / event-diff). No SSE consumption.
- Write ownership per minister is defined in `design/ministers/<name>.md`. Only Labor issues pawn-allocation writes ([labor.md](ministers/labor.md)).
- Upstream is GPL-3.0; we link only via HTTP, never in-process.
