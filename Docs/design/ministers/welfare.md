# Minister of Welfare — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M5 — feeder advisor. Hosts CMO and Trade sub-blocks until those advisors graduate.
>
> ⚠️ **Pivot translation needed.** Pre-pivot `goal_id` / HTN vocabulary maps to `advice_type` under the assisted-gameplay model ([`../../DESIGN.md`](../../DESIGN.md)). Full rewrite when the M5 slice opens.

---

## Domain

Mood, recreation, schedules, relationships, apparel. Medical triage and trade coordination as embedded modules.

---

## goal_id enum

```csharp
public enum WelfareGoal
{
    PreventMentalBreak,       // colonist at high break risk
    ImproveGeneralMood,       // mood distribution trending down
    ManageSchedules,          // work/sleep/joy balance
    UpgradeRecreation,        // rec buildings missing or insufficient
    ManageApparel,            // warmth, quality, beauty
    HandleSocialConflict,     // relationship crisis, lovin' failure
    // CMO module
    TriageMedical,            // wounded or sick colonist needs treatment
    ManageSurgeryQueue,       // scheduled surgeries
    // Prisoner module
    RecruitPrisoner,          // open/accelerate recruitment of a high-value prisoner
    // Trade module
    InitiateCaravan,          // send caravan to faction
    ManageGoodwill            // gift or appease a faction
}
```

---

## Briefing

Key fields:
- `MoodDistribution`: colonists by tier (happy/fine/at risk/break)
- `BreakRiskColonists`: list with mood level, primary mood modifier, time-to-break estimate
- `RecreationCapacity`: joy buildings available, type coverage, utilisation
- `ApplianceState`: heaters, coolers, hospital beds by room
- `RelationshipMap`: pairs with tension, recent social events
- `ScheduleEfficiency`: work hours, joy hours, sleep hours by colonist
- `RecentBreaks`: last 3 mental breaks — who, trigger, outcome
- **CMO sub-block:** `Wounded`, `Sick` (with `ImmunityProgress` % and `SeverityProgress` % per pawn — the immunity race), `SurgeryQueue`, `MedStock`, `HospitalBeds`
- **Prisoner sub-block:** `PrisonerCount`, `PrisonerDetails` (name, key skills, traits, health, current mood, resistance remaining) — recruitment pipeline
- **Trade sub-block:** `FactionGoodwill`, `TraderApproaching`, `CaravanOpportunities`, `TradeSurplus`
- **Schedule sub-block:** `Priority1Unset` (colonists missing Firefighting/Patient/Bed Rest at priority 1)

---

## Rules layer (target ~60% coverage)

| Rule | Condition | Output |
|---|---|---|
| `break_risk_immediate` | Any colonist at extreme break risk (mood < -20) | PreventMentalBreak (High) |
| `break_risk_elevated` | ≥2 colonists at minor break risk | ImproveGeneralMood (Medium) |
| `schedule_no_joy` | Any colonist with 0 joy hours in schedule | ManageSchedules |
| `rec_coverage_missing` | <50% of colonists' joy needs met by rec buildings | UpgradeRecreation (Medium) |
| `apparel_warmth_fail` | Any colonist with apparel warmth below biome winter minimum | ManageApparel (High) |
| `apparel_quality_low` | Any colonist with all apparel below Normal quality | ManageApparel (Low) |
| `triage_wounded` | Any colonist health < 70% | TriageMedical (High) |
| `surgery_queue` | SurgeryQueue non-empty AND doctor available | ManageSurgeryQueue |
| `trader_approaching` | TraderApproachingInDays < 2 | InitiateCaravan (Medium) — surface for trade decisions |
| `goodwill_low` | Any allied faction goodwill < 25 | ManageGoodwill (Low) |
| `immunity_losing_race` | Any sick pawn: SeverityProgress > ImmunityProgress | TriageMedical (Critical) — pawn will die without intervention |
| `prisoner_recruit_candidate` | PrisonerCount > 0 AND any prisoner has skill ≥ 6 in a gap role AND resistance low | RecruitPrisoner (Medium) |
| `priority_1_unset` | Any colonist missing Firefighting or Patient at priority 1 | ManageSchedules (Medium) — colony-level safety baseline |

**Escalates when:**
- Specific break-risk colonist intervention (what action would most help this specific pawn?)
- Complex social conflict resolution
- Apparel program decisions (what to craft, in what order)
- Trade composition decisions (what to sell, what to buy)
- Caravan timing and destination

---

## CMO module

Until CMO graduates, Welfare handles medical triage with rules covering the common case:

Medical rules:
- `bleed_out_risk`: any colonist bleeding AND doctor available → TriageMedical (Critical if outdoors during raid, High otherwise)
- `infection_detected`: infection detected → TriageMedical (High), flag antibiotic use
- `surgery_standard`: scheduled surgery, colonist stable → ManageSurgeryQueue (Medium)

Medical escalates for:
- Prosthetics/bionics decisions
- Surgery risk/benefit trade-offs
- Medicine allocation when scarce
- Mass casualty events (raid aftermath)

## Trade module

Until Trade Minister graduates:
- Welfare tracks faction goodwill and caravan opportunities as briefing fields.
- Trade rules fire on approaching traders and low goodwill.
- Complex caravan composition and faction strategy escalate.

---

## RIMAPI writes owned

- Schedule configuration (work/sleep/joy hours per colonist)
- Drug policy configuration
- Apparel bills (in workshop — crafting queue)
- Medical bills
- Faction gift orders (goodwill)
- Caravan formation

Labor requests posted:
- Craft apparel (Crafting labor)
- Medical treatment (Medical labor)
- Construct joy buildings (Construction labor)
- Haul medicine (Hauling labor)

---

## Success metrics

- No colonist mental break more than once per quadrum on average.
- Mood distribution: >80% of colonists at Fine or above on any given day.
- No colonist death from preventable medical issue (infected wound without medicine, bleeding without treatment).
- Escalation rate below 40% after rule refinement.

---

## Open questions / TODO

- [ ] CMO graduation criteria: when does medical reasoning justify its own minister? (Likely: when CMO sub-block rules + escalations exceed Agriculture-level complexity)
- [ ] Trade graduation criteria: when does caravan strategy justify its own minister?
- [ ] How does Welfare handle ideology-specific mood modifiers? (Deferred until Ideology DLC scope)
- [ ] Recreation building priority: which joy buildings first? (Horseshoe → chess → TV — define order in rules)
- [ ] Medical supply chain: who ensures medicine stockpile? Welfare flags need; who actually orders/crafts it?
- [ ] Relationship intervention: can the agent force social interactions? (Check RIMAPI endpoint coverage)
- [ ] Night Owl / trait-schedule mismatches: Night Owl colonists on day schedules lose mood and productive hours. Add `TraitScheduleConflicts` field to Schedule sub-block and a rule. Out of Y1-Y2 survival scope but a meaningful Y2 optimization.
- [ ] Prisoner recruitment — **resolved for briefing and rules above**. Open: what is "gap role"? Needs a SkillGaps field (cross-reference from Labor briefing when it exists, or derive from colonist roster here).
