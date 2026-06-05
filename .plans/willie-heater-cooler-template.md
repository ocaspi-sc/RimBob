# Willie — Heater/Cooler Placement Templates

> Follow-up from WD3 (welfare-rules-slice-d.md). `temperature_comfort` rule emits
> `RequestBuild(Heater|Cooler, RoomClass.Barracks)`. Willie's solver has no template for
> these target classes, so `ForSpec` routes to `BarracksTemplate` (wrong: produces a
> beds-only blueprint). Fix: add dedicated `HeaterTemplate`/`CoolerTemplate` that are
> resolved via `TargetClass`-keyed lookup before the `RoomClass` fallback path.

---

## Root Cause

`RoomTemplateSet.ForSpec` resolves templates by `RoomClass`. When the welfare rule sends
`spec.RoomClass = Barracks, spec.TargetClass = Heater`, `ForSpec` returns `BarracksTemplate`,
which produces a beds-only blueprint — wrong for a heater request.

`RoomClassForTarget` has no case for `BuildingClass.Heater`/`Cooler`, so any caller that
omits `spec.RoomClass` gets a null template and a `NoFit(NoDrafts)` result.

Fix: add a `TargetClass`-keyed primary lookup to `ForSpec` so target-specific templates
are resolved before the generic `RoomClass` path.

---

## Scope

**New files:**
- `Src/Ministers/Willie/Placement/Templates/HeaterTemplate.cs`
- `Src/Ministers/Willie/Placement/Templates/CoolerTemplate.cs`

**Edited:**
- `Src/Ministers/Willie/Placement/IRoomTemplate.cs` — add optional `BuildingClass? TargetClass` property
- `Src/Ministers/Willie/Placement/RoomTemplateSet.cs` — add `templatesByBuildingClass` dict, update `ForSpec`, update `RoomClassForTarget`, add both templates to `Default`
- `Src/Tests/Willie/RoomTemplateSetTests.cs` — add 4 new test cases

**No changes to Welfare** — the welfare rule's `RoomClass: Barracks` hint is now overridden
by the `TargetClass` primary path, so it routes correctly without touching `Welfare/Rules.cs`.

---

## Design

### `IRoomTemplate` — optional `TargetClass` property

```csharp
public interface IRoomTemplate
{
    RoomClass RoomClass { get; }

    // When set, RoomTemplateSet resolves this template via BuildingClass before the RoomClass path.
    // Existing templates omit this; only appliance-specific templates declare it.
    BuildingClass? TargetClass => null;

    string Label { get; }
    RectSize? SizeFor(CapacityNeed? need);
    RoomShell BuildShell(RectSize interior, DoorSide door);
}
```

All existing implementations get the default `null` automatically — no edits needed for
FreezerTemplate, BedroomTemplate, etc.

### `RoomTemplateSet` — `TargetClass` primary lookup

```csharp
private readonly IReadOnlyDictionary<RoomClass, IRoomTemplate> templatesByRoomClass;
private readonly IReadOnlyDictionary<BuildingClass, IRoomTemplate> templatesByBuildingClass;

public RoomTemplateSet(IReadOnlyList<IRoomTemplate> templates)
{
    // TargetClass-specific templates are indexed separately; exclude from RoomClass dict
    // to avoid displacing room-class templates (e.g. HeaterTemplate.RoomClass == Barracks
    // must not shadow BarracksTemplate in the RoomClass lookup).
    templatesByRoomClass = templates
        .Where(t => !t.TargetClass.HasValue)
        .GroupBy(t => t.RoomClass)
        .ToDictionary(g => g.Key, g => g.First());

    templatesByBuildingClass = templates
        .Where(t => t.TargetClass.HasValue)
        .ToDictionary(t => t.TargetClass!.Value);
}

public IRoomTemplate? ForSpec(PlacementSpec spec)
{
    // TargetClass-specific templates take priority over the generic RoomClass path.
    if (templatesByBuildingClass.TryGetValue(spec.TargetClass, out IRoomTemplate? specific))
        return specific;

    RoomClass? roomClass = spec.RoomClass ?? RoomClassForTarget(spec.TargetClass);
    return roomClass is not null &&
        templatesByRoomClass.TryGetValue(roomClass.Value, out IRoomTemplate? template)
            ? template
            : null;
}
```

`RoomClassForTarget` additions:
```csharp
BuildingClass.Heater => RoomClass.Barracks,
BuildingClass.Cooler => RoomClass.Barracks,
```

`Default` additions: `new HeaterTemplate()` and `new CoolerTemplate()`.

### `HeaterTemplate`

- `RoomClass = RoomClass.Barracks` — tells `ReuseExistingFootprintGenerator` to look for
  existing barracks rooms. A heater is an interior fixture; the reuse generator CAN place it
  inside an existing barracks because `IsReusableInteriorAsset` does NOT exclude "heater" role.
- `TargetClass = BuildingClass.Heater` — registers in `templatesByBuildingClass`.
- Shell: 3×3 interior, single `TemplateAsset("heater", "Heater", null, new MapCell(2, 2), 0)`,
  concrete floor. Heater at room center is standard RimWorld placement.
- `SizeFor`: always returns `new RectSize(3, 3)` — one appliance serves any occupant count.

### `CoolerTemplate`

- `RoomClass = RoomClass.Barracks` — same room-type hint.
- `TargetClass = BuildingClass.Cooler`.
- Shell: 3×3 interior, cooler on exterior wall opposite the door (same pattern as `FreezerTemplate`
  but without food-capacity sizing). Concrete floor.
- `SizeFor`: always returns `new RectSize(3, 3)`.
- Note: `ReuseExistingFootprintGenerator.IsReusableInteriorAsset` EXCLUDES "cooler" role, so
  the reuse path produces no drafts for `CoolerTemplate` — correct, since coolers are exterior
  wall fixtures and cannot be inserted into existing room interiors.

---

## Placement Generator Behavior

| Generator | HeaterTemplate | CoolerTemplate |
|-----------|----------------|----------------|
| `TemplateAnchoredGenerator` | Builds new 3×3 heater room near anchor | Builds new 3×3 room + cooler on wall |
| `ReuseExistingFootprintGenerator` | Places heater inside existing barracks ✓ | No reusable assets (cooler excluded) — empty |
| `LargestEmptyRectangleGenerator` | Uses template shell normally | Uses template shell normally |

---

## Tests to Add (RoomTemplateSetTests.cs)

```
Default_ResolvesHeaterByTargetClass      // Spec(Heater, null)  → HeaterTemplate
Default_ResolvesCoolerByTargetClass      // Spec(Cooler, null)  → CoolerTemplate
Default_HeaterTargetClassOverridesBarracksRoomClass  // Spec(Heater, Barracks) → HeaterTemplate (not BarracksTemplate)
Default_BarracksStillResolvesForBedRequest           // Spec(Bed, Barracks) → BarracksTemplate
```

---

## Acceptance

- `dotnet test Src/Tests/RimBob.Tests.csproj` green.
- `RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Heater, RoomClass.Barracks))` → `HeaterTemplate`.
- `RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Cooler, null))` → `CoolerTemplate`.
- `RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Bed, RoomClass.Barracks))` → `BarracksTemplate` (no regression).
- No changes to `Src/Common/Advice/FlagRequests.cs` or `Welfare/`.

---

## Files

**New:** `Templates/HeaterTemplate.cs`, `Templates/CoolerTemplate.cs`  
**Edited:** `IRoomTemplate.cs`, `RoomTemplateSet.cs`, `RoomTemplateSetTests.cs`

---

## Summary (landed 2026-06-05)

**Motivation.** Welfare's `temperature_comfort` rule (landed `29efd3b`) emits `RequestBuild(Heater|Cooler, RoomClass.Barracks)`. Before this slice, Willie's solver had no target-class-aware template for these building classes: `ForSpec` fell through to `BarracksTemplate` and produced beds-only blueprints for a heater request — wrong placement entirely.

**Scope shipped.**
- `IRoomTemplate` interface: added default `BuildingClass? TargetClass => null` property (C# default interface implementation; all existing templates unaffected).
- `RoomTemplateSet`: added `templatesByBuildingClass` dictionary; `ForSpec` now checks `TargetClass`-keyed templates first, before the `RoomClass` fallback path; `RoomClassForTarget` maps `BuildingClass.Heater|Cooler → RoomClass.Barracks`; both new templates added to `Default`.
- `HeaterTemplate`: `RoomClass.Barracks` (so `ReuseExistingFootprintGenerator` looks for existing barracks to drop the heater into), `TargetClass.Heater`, 3×3 interior, single heater fixture at (2, 2).
- `CoolerTemplate`: `RoomClass.Barracks`, `TargetClass.Cooler`, 3×3 interior, cooler on exterior wall opposite the door (same pattern as `FreezerTemplate`).
- 4 new `RoomTemplateSetTests` cases covering all four acceptance conditions.

**How to verify.**
- `dotnet test Src/Tests/RimBob.Tests.csproj` → 573 passed, 0 failed.
- `RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Heater, RoomClass.Barracks))` → `HeaterTemplate` (welfare rule's hint now correctly overridden by TargetClass path).
- `RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Bed, RoomClass.Barracks))` → `BarracksTemplate` (no regression).
- Live: trigger a cold-colony `temperature_comfort` cycle; Willie's build queue should emit a `HeaterTemplate`-sourced blueprint rather than a no-fit or a barracks-beds blueprint.

**No changes to:** `FlagRequests.cs`, `Welfare/`, or any other minister.

**Codex run:** `20260605-125028-willie-heater-cooler-template` · branch `codex/prompt-20260605-125028-willie-heater-cooler-template` · landed commit `adfda67`
