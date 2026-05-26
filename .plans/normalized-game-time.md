# Normalized Game Time

## Decision

Normalize RimBob game dates around elapsed colony time from game start. RIMAPI's
raw date string remains source evidence, but player-facing, briefing, advice,
snapshot, replay, and dashboard fields should use a shared game-time model.

## Contract

Use `game_tick` as the numeric source of truth. RimWorld currently advances one
game day per `60_000` ticks.

```csharp
total_days = game_tick / 60_000.0;
completed_days = game_tick / 60_000;
colony_day = completed_days + 1;
colony_year = completed_days / 60 + 1;
day_of_year = completed_days % 60 + 1;
```

The normalized object should carry:

- `game_tick`: authoritative monotonic tick from `/api/v1/game/state`.
- `total_days`: fractional elapsed days since game start.
- `completed_days`: integer floor of elapsed full days since game start.
- `colony_day`: one-based absolute colony day for player-facing labels.
- `colony_year`: one-based colony year derived from 60-day RimWorld years.
- `day_of_year`: one-based day inside the current colony year.
- `quadrum`, `quadrum_day`, and `hour`: parsed from RIMAPI's date string when
  available.
- `rimworld_year`: the raw calendar year from RIMAPI, retained only for
  diagnostics/source truth.
- `raw_rim_world_date`: original RIMAPI string.
- `label`: compact display such as `Y1 D6, Aprimay 5, 14h`.

`quadrum_day` is the RimWorld calendar display day. `colony_day` is absolute
elapsed colony day. Do not use a single ambiguous `day` property for both.

## Implementation Slice

1. Add a shared `GameDate` or equivalent record in the common/state contract.
2. Replace `DateStamp.Year`/`Day` call sites with explicit normalized fields.
3. Move `Y...D...` formatting into one shared formatter used by Mayor, Chef,
   LLM normalization, replay fixtures, and dashboard.
4. Update snapshot, minister output, advice, replay, and dashboard type mirrors;
   use `updated_game_date` for Mayor agenda stamps and `issued_game_date` for
   advice/feedback stamps.
5. Update tests and fixtures to expect normalized colony time.
6. Wipe persisted latest snapshots and minister-output files after the schema
   bump, then regenerate from live state.

## Compatibility

This is a wire and persistence schema change: no compat code; wipe-and-regen on
upgrade. Keep general malformed-input handling, but do not add old-shape readers
for removed date fields.

## Non-Goals

- Do not change UTC operational timestamps such as `generated_at`,
  `captured_at`, log write times, build datetimes, or replay capture times.
- Do not infer game time from wall clock.
- Do not make RIMAPI's `5500` calendar year player-facing except in raw/debug
  views.
