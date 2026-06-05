# Minister of Welfare - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Welfare is a feeder advisor with deterministic rules and LLM escalation for Mood & Needs judgment calls. This doc records durable scope and open boundaries, not final enum values or implementation rules.

---

## Domain

Welfare keeps the minister name and owns the **Mood & Needs** domain:

- Mood and break risk as explained by needs, thoughts, recreation, comfort, beauty, sleep, and schedules.
- Relationship and social-conflict pressure when it creates mood risk.
- Guest/visitor comfort, lodging quality, and hospitality social pressure when hosting affects mood, relationship pressure, or break risk.
- Tame animal living-condition pressure when care evidence shows missing pens, barns, beds, temperature-safe shelter, cleanliness-sensitive rest areas, or similar suffering.
- Ideology mood pressure until it justifies a split.
- Apparel warmth/comfort as mood-and-needs pressure, with Industry owning production.

Medical is now treated as its own subsystem in the cabinet model, though Welfare and Medical will overlap on mood effects from pain, illness, hospital quality, and care access. Medical owns the treatment decision; Welfare owns the mood impact and break-risk framing. Reconcile the remaining overlap before Medical or deeper Welfare/Medical slices.

Chef owns the nutrition chain. Welfare can flag hunger, bad meal mood, or nutrient-paste pressure as Mood & Needs evidence, but requests Chef when the actual fix is meals, crops, hunting, cooking, or storage.

Trade/Economy is no longer a Welfare sub-block in the target cabinet. Economy owns trade and wealth once scheduled.

Guest/visitor stewardship defaults to Welfare for comfort, lodging quality, and social pressure. Economy owns guests as trade, caravan, goodwill, or diplomacy opportunities; Willie owns the physical guest-room work; Defense owns active threat response.

Tame animal welfare is in scope as a living-condition requester, not as global animal management. Welfare can raise pens, barns, animal beds, temperature-safe shelter, medical resting spots, and cleanliness-sensitive rest areas as requests to Willie. Chef owns feed, starvation, and slaughter pressure; Economy owns sale, trade, and herd economics; Defense owns dangerous or combat animals; Medical owns treatment.

---

## Implementation Status

Welfare is a `Suggest`-mode minister with deterministic rules first and dashboard-confirmed LLM escalation second. Slice A wired `break_risk` and `shelter_floor`: break risk produces player-facing mood triage, while missing sleeping shelter emits a `building_request` to Willie for a basic barracks with enough beds. Slice B keeps `break_risk`/`shelter_floor`, adds `recreation_gap` and `comfort_beauty`, and converts the rules layer to independent rule emissions so simultaneous mood pressures surface together instead of being hidden behind the first matched rule. Slice C adds the Welfare LLM path: when no deterministic rule matched but material unwired mood pressure remains, Welfare emits `Escalate("unexplained_mood_pressure")`; the rules trace records that pending escalation, and `Run LLM` is the dashboard confirmation that runs provider work. Slice D adds the `temperature_comfort` rule: a `Temperature` thought group past the offset floor emits a cold/hot-routed `Heater`/`Cooler` `building_request` to Willie (ambiguous labels request player attention instead of guessing the appliance), and `Temperature` is excluded from the escalation bucket so mild weather no longer escalates. Welfare does not own Apply; any eventual `place_blueprint` control belongs to Willie's advice after the Placement Solver produces options.

Live deterministic rule signals:

- Break risk.
- Sleeping shelter floor: bed deficit or unroofed sleeping rooms.
- Recreation coverage and low-joy pressure.
- Comfort/beauty pressure, including table/dining setup requests for concrete table thoughts.
- Temperature comfort: cold or hot mood-thought pressure, routed to Willie `Heater`/`Cooler` builds (ambiguous labels request player attention).

Likely follow-on advice areas: schedule problems that are visible and actionable; apparel warmth risk when live data supports it; social conflict, guest, animal, or ideology pressure only when it creates clear advice.

---

## Available Source Signals

The source briefing is consumed by Welfare's rules and LLM path and remains visible through `GET /api/briefings/welfare/latest` and the dashboard's Welfare > Briefing view. Welfare prompt, RAG, raw LLM output, and manual LLM ingestion surfaces are wired. Welfare-owned Apply actions are still not implemented.

Available signals:

- Mood distribution from live colonist mood.
- Per-pawn wellbeing need levels: sleep, comfort, beauty, joy, fresh air, and drugs desire where the game exposes it.
- Active mood thoughts from the RimBob RIMAPI fork: `def_name`, `label`, `mood_offset`, `stage_index`.
- Room evidence from `/api/v1/map/rooms`: role, temperature, cell count, prison flag, doorway/open-roof/map-edge signals, contained bed ids, and room stats for impressiveness, beauty, cleanliness, space, and wealth.
- Rule-facing derived summaries: bed count/deficit/unroofed sleeping rooms, joy-low and recreation-source counts, and a thought digest grouped by Welfare's deterministic thought taxonomy.

Snapshot schema changes for these signals use no compat code; wipe-and-regen on upgrade.

---

## Briefing Direction

Welfare briefing should answer:

- Who is at break risk and why?
- Which need, thought, schedule, room, relationship, or ideology pressure is driving the risk?
- Are recreation and sleep needs being met?
- Are schedule settings creating avoidable mood loss?
- Are comfort, beauty, room quality, or apparel causing clear pressure?
- Are relationships/social events creating a current risk?
- Are guests/visitors creating hospitality, room-quality, or social pressure?
- Are tame animals suffering from missing pens, barns, beds, temperature-safe shelter, or rest-area conditions that should become a Willie request?
- Which requests should go to Chef, Willie, Industry, Medical, or Economy?

New exact fields belong in code/tests once their slice ships.

---

## Escalation Boundaries

Rules handle obvious mood thresholds and missing basic recreation, sleep, and temperature signals. The current fallback escalates as `unexplained_mood_pressure` only after no deterministic rule matched and there is material non-wired thought pressure (outside shelter, recreation, comfort, and temperature — i.e. social, ideology, health, or other) or low average mood. Escalate for:

- Specific pawn intervention choices.
- Complex relationship/social conflicts.
- Apparel program trade-offs.
- Tame animal living-condition trade-offs that cross feed, herd economics, defense, or medical treatment.
- Ideology-specific decisions.
- Welfare/Medical boundary cases.

---

## Resource Requests

Welfare may request:

- Chef: meals, meal quality, or nutrition-chain fixes when hunger or food mood is the driver.
- Willie: beds, recreation buildings, room improvements, comfort assets, guest rooms, animal pens, barns, animal beds, temperature-safe animal shelter, or animal medical resting spots.
- Economy: visitor trade, caravan, goodwill, or diplomacy opportunities.
- Industry: apparel or beauty/comfort goods.
- Medical: pain, wounds, disease, or care issues affecting mood.
- Labor/player attention: schedule or policy changes in Suggest mode.

Requests remain advice in MVP; they do not issue RIMAPI writes by themselves.

---

## Success Metrics

- Break-risk warnings are timely and not noisy.
- Mood distribution stays mostly stable or improving.
- Recreation and sleep gaps are surfaced before crises.
- Welfare advice stays concrete rather than generic "improve mood" prose.
- Advice names the current need/thought driver instead of hiding behind generic wellbeing language.
- Escalation rate falls as common mood patterns become rules.

---

## Open Questions / TODO

- [ ] Resolve Welfare/Medical overlap before Medical or deeper Welfare slices.
- [x] Define first Welfare rule signals for Slice A.
- [x] Define first Slice B recreation and comfort/beauty build priorities from live data.
- [ ] Decide how schedule advice interacts with deferred Labor/Auto.
- [ ] Define first tame-animal living-condition signals when that implementation starts.
- [ ] Add ideology-specific handling only when DLC scope requires it.
- [ ] Decide whether prisoner living-conditions mood pressure stays Welfare or moves later.
