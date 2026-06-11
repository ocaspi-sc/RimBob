# NC1 fixture recapture + real placement teeth

Subplan of [milestone-nc1.md](milestone-nc1.md). Status: **ACTIVE 2026-06-11** — user confirmed canonical save live; recapture subagent running. (Was ON HOLD 2026-06-09.)

## Problem

Frozen fixture [new-colony-1.colony-state.json](../Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json) at tick 538 = pristine start: `zones`/`areas` Area_Home `cell_count:0`, `bounds:null`, `cells:[]`; `buildings:[]`; `work_tables:[]`. Live save now has a **small hand-drawn Area_Home as its only base anchor**. Test world ≠ live world on the anchor that drives ALL Willie placement.

## Consequences

- `Willie_NewColony1_PlacesHighPriorityBeds` ([AdviceForNewColony1Tests.cs:224](../Src/Tests/NewColony/AdviceForNewColony1Tests.cs)) passes via synthetic solve (AlwaysReachable probe + Accepting validator + empty-area fallback) — NOT real packing into a small home.
- Real NC1 placement challenge — multi-building layout (beds+kitchen+stockpile+heater) inside one small Area_Home, respecting actual (likely non-rect) cell shape — UNTESTED.
- [willie-home-area-buildable-region-anchor.md](willie-home-area-buildable-region-anchor.md) clamp-to-bounds + deferred non-rect cell-mask is latent: bounding-box clamp can propose cells outside a hand-drawn home.

Anchor ingestion path itself verified live (`add66e7`). Only the frozen snapshot is stale.

## Scope

1. **Recapture** from live save (RimWorld running on NC1 save) → new snapshot with real Area_Home cells/bounds + any buildings. No compat; wipe-and-regen the LFS fixture. Re-run reach suite.
2. ~~**Non-rect cell-mask** in solver~~ — **DESCOPED from NC1 2026-06-11** (user): Area_Home auto-expands when rooms built outside it, so bounding-box clamp good enough for NC1. Cell-mask stays a post-NC1 option if real placements land outside hand-drawn home and that ever matters.
3. **Real-packing tests** — convert `PlacesHighPriorityBeds` off the Accepting validator; add a multi-building packing test (beds+kitchen+stockpile+heater fit, no overlap) on the real solver. Mask assertion dropped with item 2.

## Gate

~~User confirms NC1 save layout is final.~~ Cleared 2026-06-11.

## Done bar

Fixture == live save on Area_Home + buildings; real-packing test green on the real solver. (Non-rect mask: out of NC1.)
