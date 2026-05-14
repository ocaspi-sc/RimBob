# Defense Minister — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M5 — feeder advisor; flag-severity-gated tactical alerts route via this minister into the dashboard.
>
> ⚠️ **Pivot translation needed.** Pre-pivot `goal_id` / HTN vocabulary in this doc maps to `advice_type` and `tactical_alert` advice items under the assisted-gameplay model ([`../../DESIGN.md`](../../DESIGN.md)). Full rewrite when the M5 slice opens.

---

## Domain

Active threats, combat, fortifications, killbox design. Owns all pawn-in-combat decisions. Can emit Critical flags that preempt all other ministers.

---

## goal_id enum

```csharp
public enum DefenseGoal
{
    RespondToActiveRaid,      // active raid in progress
    HardenPerimeter,          // fortify walls, add turrets, improve killbox
    ManageArsenal,            // weapon upgrades, ammo, repair
    EstablishKillbox,         // build/improve the chokepoint
    MechClusterResponse,      // mechanoid cluster specific response
    SiegeResponse,            // enemy siege camp active
    PostRaidRecovery,         // triage after combat, repair breaches
    MonitorThreatTrend        // no active threat, but raid points rising
}
```

---

## Briefing

Key fields:
- `ActiveRaid`: type (tribal/pirate/mech), faction, point value, composition if scouted
- `PerimeterState`: wall integrity by sector, breach locations, fire status
- `Colonists`: shooters (name, skill, weapon, health, position), melee fighters
- `Turrets`: active count, damaged, power-online
- `KillboxState`: chokepoint locations, trap state, turret coverage
- `ThreatHistory`: last 5 raids — composition, point value, outcome, casualties
- `SeasonalThreats`: predicted incoming events
- `WealthTier`: current raid point ceiling (from wealth + colony age)

---

## Rules layer (target ~40% coverage)

Most Defense decisions require context. Rules handle the clear-cut cases.

| Rule | Condition | Output |
|---|---|---|
| `active_raid_critical` | ActiveRaid != null | RespondToActiveRaid (Critical) |
| `breach_detected` | PerimeterState has any breach | HardenPerimeter (High) |
| `no_killbox_yet` | KillboxState.Exists == false AND colonistCount >= 3 | EstablishKillbox (High) |
| `weapon_below_threshold` | Any shooter has weapon tier < 2 AND no active raid | ManageArsenal (Medium) |
| `post_raid_repair` | LastRaidEndedWithinTicks(500) AND any breach | PostRaidRecovery (High) |
| `wealth_tier_warning` | WealthTier increased AND defenseScore < wealthTier | HardenPerimeter (Medium) |

**Escalates when:**
- Active raid with unusual composition (requires tactical response)
- Mech cluster or siege (specialized doctrine)
- Killbox design decisions
- Weapon upgrade trade-offs
- Any Critical flag evaluation that involves nuance

---

## RIMAPI writes owned

- Combat orders (draft, undraft, force attack, flee)
- Trap designation
- Blueprint: fortification structures (only layout decisions — Construction builds them)
- Turret placement designation
- Killzone / no-go zone designation

Labor requests posted:
- Construct fortifications (Construction labor)
- Weapon crafting (Crafting labor)
- Medical treatment of combat casualties (Welfare/CMO labor)

---

## Critical flag authority

Defense is the only minister that can emit Critical severity flags and preempt all other ministers. This authority is used only for:
- Active raid
- Active fire spreading toward key structures
- Active infestation inside the base

All other Defense concerns emit High or lower.

---

## HTN domain

```
RespondToActiveRaid
├── killbox_available → DraftShooters, HoldKillboxPosition, ManageTurrets
├── no_killbox → DraftAll, FallbackToInterior, EmitFlag(EstablishKillbox, High)
└── escalate → LLM (unusual composition, mech/siege, overwhelming force)

HardenPerimeter
├── materials_available → PostLaborRequest(Construction, Fortify, breachZones)
├── no_materials → EmitFlag(RequestMaterials, High)
└── killbox_needs_design → escalate (LLM: layout decisions)
```

---

## Success metrics

- Zero colonist deaths from raids that were successfully defended (defended = at least one raider died)
- Killbox functional before first major raid (day ~30-45 Cassandra)
- No raid losses where defense tier exceeded raid points
- Escalation rate below 45% after rule refinement

---

## RAG retrieval profile

Topics: `["raid", "combat", "killbox", "defense", "turret", "mechanoid", "siege", "fortification", "weapons"]`
Retrieval required for: any escalated tactical decision, mech cluster response, killbox design.

---

## Open questions / TODO

- [ ] Exact weapon tier definitions (tier 1 = pistols/bows, tier 2 = bolt-action, tier 3 = chain shotgun, etc.)
- [ ] How does Defense coordinate with Construction on fortification builds? (Flag? Direct labor request?)
- [ ] Retreat logic: when does Defense recommend retreat vs hold?
- [ ] Poison ship / psychic ship handling (these are one-time events with unique responses)
- [ ] Royalty/Biotech threat variants (deferred until DLC scope added)
- [ ] How does Defense handle no-killbox early game (first week)?
