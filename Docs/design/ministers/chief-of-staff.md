# Chief of Staff — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: 6. Depends on: all five cabinet ministers operational.

---

## Role

Flag triage and conflict arbitration. The CoS sits between the cabinet and the Mayor.

- Resolves High-tier conflicts within the same tick.
- Batches Medium flags into the Mayor's daily digest.
- Can downgrade flags (with reasoning logged).
- Does NOT issue labor requests or RIMAPI writes.

---

## Escalation rate target

~70%. Arbitration is judgment by definition. Rules handle the most mechanical resolutions (Defense Critical always wins; Low flags always defer). Everything contested escalates.

---

## Known rules

- `critical_always_wins`: if any Critical flag exists, all other priority work defers to it.
- `single_high_auto_resolve`: if only one High flag is active, resolve it without LLM.
- `defer_low_when_high_active`: Low flags queue when any High or Critical is active.
- `flag_dedup`: if two flags from the same minister have identical summaries, keep the newer; drop the older.

---

## LLM output schema

```jsonc
{
  "resolutions": [
    {
      "flag_id": "...",
      "action": "forward_to_mayor | resolve | downgrade | defer",
      "priority_ruling": "food_wins | defense_wins | split",
      "rationale": "..."
    }
  ],
  "mayor_digest": {
    "summary": "...",
    "flags": [ /* Medium flags batched */ ],
    "strategic_tensions": [ /* patterns worth Mayor attention */ ]
  },
  "flags": [ /* AgentFlag[] CoS emits to Mayor */ ],
  "notes": "..."
}
```

---

## Open questions / TODO

- [ ] Define exact resolution action enum
- [ ] Should CoS have its own briefing, or just read the flag channel?
- [ ] How does CoS handle three-way contention (Construction, Defense, and Food all want the same colonist at High)?
- [ ] Flag aging: should Low flags auto-escalate to Medium after N in-game hours of being ignored?
