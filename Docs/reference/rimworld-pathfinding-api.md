# RimWorld Pathfinding / Region / Reachability API Reference

> Reference for RimBob/RIMAPI work that wraps these APIs over HTTP.
> Signatures verified against Krafs.Rimworld.Ref NuGet packages
> (1.5.4104 and 1.6.4518) and real callsites in RIMAPI and RimMind on
> 2026-05-27. RimWorld itself is closed-source; if Ludeon refactors,
> regenerate this doc from updated callsites or the Krafs ref packages.
>
> **Critical 1.5 → 1.6 breaking change:** `PathFinder` moved from
> `Verse.AI.PathFinder` to `Verse.PathFinder`, and `FindPath` was renamed
> `FindPathNow` with a changed signature. See §Pathfinding for details.

---

## Quick map: cheap vs expensive

| API | Cost class | Typical use |
|---|---|---|
| `map.reachability.CanReach(...)` | O(1) amortized — region cache | Per-tick "can colonist reach X?" guards |
| `map.reachability.CanReachColony(cell)` | O(1) amortized | Reachability from any colony pawn spawn |
| `RegionGrid.GetValidRegionAt_NoRebuild(cell)` | O(1) direct array lookup | Translate cell → Region for BFS setup |
| `RegionTraverser.BreadthFirstTraverse(...)` | O(regions traversed) — cheap, no A* | Coarse "hop count" distance, region flood-fill |
| `PathFinder.FindPath / FindPathNow(...)` | O(N log N) A* — expensive | Exact walkable cost; call sparingly |

---

## Reachability — `map.reachability.CanReach`

`map.reachability` is of type `Verse.Reachability` (public field on `Verse.Map`, same in 1.5 and 1.6).

### Signatures (identical across 1.5 and 1.6)

```csharp
// Overload 1 — TraverseMode + Danger (convenience, allocates TraverseParms internally)
bool CanReach(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode,
              TraverseMode traverseMode, Danger maxDanger);

// Overload 2 — explicit TraverseParms (preferred for controlled traversal)
bool CanReach(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode,
              TraverseParms traverseParms);
```

Both overloads are on the instance `map.reachability`. `LocalTargetInfo` is implicitly constructible from `IntVec3`, `Thing`, or `TargetInfo`.

### Non-local variants (cross-map targets)

```csharp
bool CanReachNonLocal(IntVec3 start, TargetInfo dest, PathEndMode peMode,
                      TraverseMode traverseMode, Danger maxDanger);
bool CanReachNonLocal(IntVec3 start, TargetInfo dest, PathEndMode peMode,
                      TraverseParms traverseParms);
```

### Convenience helpers

```csharp
bool CanReachColony(IntVec3 cell);                           // reachable from any colony pawn?
bool CanReachMapEdge(IntVec3 cell, TraverseParms tp);
bool CanReachUnfogged(IntVec3 cell, TraverseParms tp);
bool CanReachFactionBase(IntVec3 cell, Faction faction);
```

### Pawn-side shortcut (via `ReachabilityUtility`, extension-style)

RimMind uses the pawn-side form:

```csharp
// Called as pawn.CanReach(dest, peMode, danger)
// Actually: ReachabilityUtility.CanReach(Pawn pawn, LocalTargetInfo dest,
//               PathEndMode peMode, Danger maxDanger, bool canBash,
//               bool traverseViaLord, TraverseMode mode);
```

Callsite: `C:\dev\RimMind\Source\RimMind\Tools\DraftedPawnTools.cs:45`

```csharp
if (!pawn.CanReach(dest, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
```

### Semantics

- Backed by a `ReachabilityCache` keyed on `(District, District, TraverseParms)`.
- Cache is invalidated on region rebuild (`regionDirtyer`). Safe to call per-tick.
- Returns `false` for out-of-bounds cells, `null` regions, and when `TraverseParms` requires crossing barriers the region graph disallows.

### Example — RIMAPI endpoint wrapper

```csharp
var tp = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
bool ok = map.reachability.CanReach(from, new LocalTargetInfo(to), PathEndMode.OnCell, tp);
```

### Callsite index

| File | Line | Form used |
|---|---|---|
| `C:\dev\RimMind\Source\RimMind\Tools\DraftedPawnTools.cs` | 45 | `pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly)` |
| `C:\dev\RimMind\Source\RimMind\Tools\WorkTools.cs` | 735 | `map.reachability.CanReachColony(thing.Position)` |
| `C:\dev\RimMind\Source\RimMind\Tools\WorkTools.cs` | 740 | `map.reachability.CanReachColony(designation.target.Cell)` |

---

## Region lookup — `map.regionGrid.GetValidRegionAt_NoRebuild`

`map.regionGrid` is of type `Verse.RegionGrid` (public field on `Verse.Map`, same in 1.5 and 1.6).

### Key lookup methods

```csharp
// Preferred for BFS setup: returns null if region not yet built or invalid.
// Does NOT trigger a rebuild. Fast O(1) array lookup.
Region GetValidRegionAt_NoRebuild(IntVec3 c);

// Triggers rebuild if dirty. Slower; avoid in tight loops.
Region GetValidRegionAt(IntVec3 c);

// Returns region even if marked invalid (use only for debug/special cases).
Region GetRegionAt_NoRebuild_InvalidAllowed(IntVec3 c);
```

### Iteration properties

```csharp
// 1.5: IEnumerable<Room> allRooms (lowercase field, not a property)
// 1.6: IReadOnlyList<Room> AllRooms (property, PascalCase)
IEnumerable<Region> AllRegions { get; }                     // triggers rebuild if dirty
IEnumerable<Region> AllRegions_NoRebuild_InvalidAllowed { get; }
Region[] DirectGrid { get; }                                // flat array, map.cellCount long
```

> **1.5 vs 1.6 difference — AllRooms:**
> - 1.5: `map.regionGrid.allRooms` — lowercase field, type `List<Room>`
> - 1.6: `map.regionGrid.AllRooms` — PascalCase property, type `IReadOnlyList<Room>`
>
> RIMAPI handles this with `#if RIMWORLD_1_5` / `#elif RIMWORLD_1_6` guards.

### Semantics

- Returns `null` for out-of-bounds cells and cells not yet assigned a region.
- Two cells in the same open room typically share the same `Region` or are in adjacent regions linked via `RegionLink`.
- Caller must null-check the returned `Region` before use.

### Callsite index

| File | Line | Form used |
|---|---|---|
| `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Helpers\MapHelper.cs` | 396 | `map.regionGrid.allRooms` (1.5) |
| `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Helpers\MapHelper.cs` | 420 | `map.regionGrid.AllRooms` (1.6) |
| `C:\dev\RimMind\Source\RimMind\Tools\ColonistTools.cs` | 419 | `map.regionGrid.AllRooms` |
| `C:\dev\RimMind\Source\RimMind\Tools\ColonyTools.cs` | 168 | `map.regionGrid.AllRegions` |
| `C:\dev\RimMind\Source\RimMind\Tools\EnvironmentTools.cs` | 19 | `map.regionGrid.AllRooms` |
| `C:\dev\RimMind\Source\RimMind\Tools\SemanticTools.cs` | 54 | `map.regionGrid.AllRooms` |

---

## Region distance — `RegionTraverser.BreadthFirstTraverse`

All three overloads are static methods on `Verse.RegionTraverser`.

### Signatures (identical across 1.5 and 1.6)

```csharp
// Overload A — start from cell on a map (translates cell → Region internally)
static void BreadthFirstTraverse(
    IntVec3 start,
    Map map,
    RegionEntryPredicate entryCondition,
    RegionProcessor regionProcessor,
    int maxRegions = 999999,
    RegionType traversableRegionTypes = RegionType.Set_Passable);

// Overload B — start from a Region, use cached delegate object
static void BreadthFirstTraverse(
    Region start,
    RegionProcessorDelegateCache processorCache,
    int maxRegions = 999999,
    RegionType traversableRegionTypes = RegionType.Set_Passable);

// Overload C — start from a Region, raw delegates (most commonly used for custom BFS)
static void BreadthFirstTraverse(
    Region start,
    RegionEntryPredicate entryCondition,
    RegionProcessor regionProcessor,
    int maxRegions = 999999,
    RegionType traversableRegionTypes = RegionType.Set_Passable);
```

### Delegate shapes

```csharp
// RegionEntryPredicate — called before entering a region.
// Parameters: (Region from, Region to) — return true to allow traversal into `to`.
delegate bool RegionEntryPredicate(Region from, Region to);

// RegionProcessor — called for each region reached (including start).
// Return true to STOP traversal (like a break in a loop).
delegate bool RegionProcessor(Region reg);
```

### Example — region-BFS hop counter

```csharp
Region startReg = map.regionGrid.GetValidRegionAt_NoRebuild(from);
Region endReg   = map.regionGrid.GetValidRegionAt_NoRebuild(to);
if (startReg == null || endReg == null) return Unreachable();
if (startReg == endReg) return new Result(0, true);

int regionsTraversed = 0;
bool found = false;

RegionTraverser.BreadthFirstTraverse(
    startReg,
    entryCondition: (fromReg, toReg) => toReg.Allows(traverseParms, isDestination: false),
    regionProcessor: reg => {
        regionsTraversed++;
        if (reg == endReg) { found = true; return true; }   // stop
        return regionsTraversed >= maxCost;                  // cap
    },
    maxRegions: maxCost);

return found ? new Result(regionsTraversed, true) : Unreachable();
```

> Note: `regionProcessor` is called for `startReg` first. If `startReg == endReg`
> you never reach the processor and should short-circuit before calling (as above).

### Semantics

- BFS is non-recursive and worker-pooled internally (`RegionTraverser.RecreateWorkers`).
- `maxRegions` is a hard cap on the number of regions dequeued (not visited). Default 999999.
- `traversableRegionTypes` defaults to `RegionType.Set_Passable` (Normal + Portal + Fence).
- Passable `RegionType` values: `Normal`, `Portal`, `Fence`. Impassable: `ImpassableFreeAirExchange`.
- Use `RegionType.Set_All` to traverse all region types (rarely needed).

---

## Pathfinding — `map.pathFinder.FindPath` / `FindPathNow`

> **Breaking change in 1.6:** The field type changed from `Verse.AI.PathFinder` to
> `Verse.PathFinder`, and the method was renamed from `FindPath` to `FindPathNow`.
> The 1.6 pathfinder also became async-capable (job-batched); `FindPathNow` forces
> synchronous completion.

### 1.5 — `Verse.AI.PathFinder` (field: `map.pathFinder`)

```csharp
// Overload 1 — per-pawn (uses pawn's TraverseParms internally)
PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, Pawn pawn,
                  PathEndMode peMode,
                  PathFinderCostTuning costTuning = null);

// Overload 2 — explicit TraverseParms (use this for pawn-agnostic queries)
PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms,
                  PathEndMode peMode,
                  PathFinderCostTuning costTuning = null);
```

### 1.6 — `Verse.PathFinder` (field: `map.pathFinder`)

```csharp
// Overload 1 — per-pawn
PawnPath FindPathNow(IntVec3 start, LocalTargetInfo dest, Pawn pawn,
                     PathFinderCostTuning? costTuning,     // Nullable<PathFinderCostTuning> in 1.6
                     PathEndMode peMode);

// Overload 2 — explicit TraverseParms
PawnPath FindPathNow(IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms,
                     PathFinderCostTuning? costTuning,
                     PathEndMode peMode,
                     IPathGridCustomizer customizer = null);
```

> Note the parameter order changed between versions: in 1.5 `peMode` is before `costTuning`;
> in 1.6 `costTuning` is before `peMode`. RIMAPI must use `#if` guards.

### `PawnPath` — return type, pooling, and disposal

`PawnPath` (namespace `Verse.AI`) is **pooled and `IDisposable`**. Always dispose it:

```csharp
// 1.5
using var path = map.pathFinder.FindPath(from, to, traverseParms, peMode);
if (path == PawnPath.NotFound) return Unreachable();       // static sentinel
int cost = (int)path.TotalCost;                            // TotalCost is float
return new Result(cost, true);
// path.Dispose() called automatically at end of using block

// 1.6
using var path = map.pathFinder.FindPathNow(from, to, traverseParms, null, peMode);
if (path == PawnPath.NotFound) return Unreachable();
int cost = (int)path.TotalCost;
return new Result(cost, true);
```

Key `PawnPath` members (both 1.5 and 1.6 unless noted):

| Member | Type | Notes |
|---|---|---|
| `TotalCost` | `float` (property) | Sum of movement costs along the path |
| `Found` | `bool` (property) | `false` if not found |
| `NotFound` | `PawnPath` (static property) | Sentinel; compare with `==` not `null` |
| `NodesLeftCount` | `int` (property) | Remaining unread nodes |
| `NodesReversed` | `List<IntVec3>` (property) | All cells, end → start |
| `Dispose()` | `void` | Returns to `PawnPathPool` — **always call** |
| `Finished` | `bool` (property, 1.6 only) | Additional completion flag |

**Pool mechanism:** `map.pawnPathPool` (`Verse.PawnPathPool`/`Verse.AI.PawnPathPool`) holds a
pre-allocated pool of `PawnPath` objects. `FindPath`/`FindPathNow` takes one from the pool;
`Dispose()` returns it. Not disposing leaks pool slots until GC collects.

---

## Supporting types

### `Verse.TraverseParms`

Struct. Two static factory methods (both 1.5 and 1.6 differ by one extra bool in 1.6):

```csharp
// 1.5
static TraverseParms For(Pawn pawn,
    Danger maxDanger      = Danger.Deadly,
    TraverseMode mode     = TraverseMode.ByPawn,
    bool canBash          = false,
    bool alwaysUseAvoidGrid = false,
    bool fenceBlocked     = false);

static TraverseParms For(TraverseMode mode,
    Danger maxDanger      = Danger.Deadly,
    bool canBash          = false,
    bool alwaysUseAvoidGrid = false,
    bool fenceBlocked     = false);

// 1.6 — adds one extra bool (position 7 for pawn-form, position 6 for mode-form)
static TraverseParms For(Pawn pawn, Danger maxDanger, TraverseMode mode,
    bool canBash, bool alwaysUseAvoidGrid, bool fenceBlocked, bool newExtraFlag);

static TraverseParms For(TraverseMode mode, Danger maxDanger,
    bool canBash, bool alwaysUseAvoidGrid, bool fenceBlocked, bool newExtraFlag1, bool newExtraFlag2);
```

Implicit conversion: `TraverseMode` implicitly converts to `TraverseParms` via `op_Implicit`.

**For RIMAPI endpoints without a specific pawn:** use the `TraverseMode` overload:

```csharp
var tp = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
```

### `Verse.TraverseMode` enum

```csharp
enum TraverseMode {
    ByPawn                          = 0,  // uses pawn's own door/fence logic
    PassDoors                       = 1,  // pass through any door (open or closed)
    NoPassClosedDoors               = 2,  // don't pass through closed doors
    PassAllDestroyableThings        = 3,  // pass through destroyable obstacles
    PassAllDestroyablePlayerOwnedThings = 4,
    NoPassClosedDoorsOrWater        = 5,
    PassAllDestroyableThingsNotWater = 6,
}
```

### `Verse.AI.PathEndMode` enum

Used for both `CanReach` and `FindPath`/`FindPathNow`. Namespace is `Verse.AI` in both 1.5 and 1.6.

```csharp
enum PathEndMode {
    None            = 0,
    OnCell          = 1,  // must reach the exact cell
    Touch           = 2,  // must reach a cell adjacent to (touching) dest
    ClosestTouch    = 3,  // reach the closest touchable cell; never fails due to end mode
    InteractionCell = 4,  // reach the thing's designated interaction cell
}
```

### `Verse.Danger` enum

```csharp
enum Danger {
    Unspecified = 0,
    None        = 1,
    Some        = 2,
    Deadly      = 3,
}
```

`Danger.Deadly` is the permissive default — allows traversal through any danger level.

### `Verse.RegionType` enum (flags)

```csharp
[Flags]
enum RegionType {
    None                    = 0,
    ImpassableFreeAirExchange = 1,   // impassable cell (e.g. solid wall)
    Normal                  = 2,     // standard walkable cell
    Portal                  = 4,     // doorway / portal region (one cell)
    Fence                   = 8,
    Set_Passable            = Normal | Portal | Fence,   // default for BFS traversal
    Set_Impassable          = ImpassableFreeAirExchange,
    Set_All                 = Set_Passable | Set_Impassable,
}
```

### `Verse.Region.Allows`

Confirmed present in both 1.5 and 1.6:

```csharp
bool Region.Allows(TraverseParms tp, bool isDestination);
```

Used as the canonical entry predicate in BFS traversal: pass `isDestination: false` for
intermediate regions, `isDestination: true` only for the target region if it matters.

---

## Gotchas

### `PawnPath` pooling

Not calling `Dispose()` (or `ReleaseToPool()` directly) leaks pool slots. Under load this
causes the pool to exhaust and pathfinder to allocate new objects per call, defeating the
pool's purpose. Always use `using var path = map.pathFinder.FindPath(...)`.

### Region cache invalidation

`GetValidRegionAt_NoRebuild` returns `null` for cells whose region has been dirtied but not
yet rebuilt. If you get `null` unexpectedly, call `GetValidRegionAt` (which triggers rebuild)
or check `map.regionAndRoomUpdater.AnythingToRebuild`. For endpoint code that runs on the
main game thread, dirty-region windows are brief; the `_NoRebuild` variant is safe for
most use-cases, but always null-check.

### Thread safety

All RimWorld APIs listed here are **main-thread-only**. RIMAPI dispatches endpoint
handler logic onto the main thread via its `GameActionQueue` before touching any Verse APIs.
Never call `CanReach`, `FindPath`, `BreadthFirstTraverse`, or `GetValidRegionAt` from a
background thread.

### 1.6 `PathFinder` namespace and method rename

- Type moved: `Verse.AI.PathFinder` → `Verse.PathFinder`
- Map field type changed accordingly: `map.pathFinder` is `Verse.AI.PathFinder` in 1.5,
  `Verse.PathFinder` in 1.6
- Method renamed: `FindPath` → `FindPathNow`
- Parameter order changed (see §Pathfinding)
- The 1.6 `Verse.PathFinder` has additional job-batching infrastructure (`FindPathNow`
  forces synchronous execution of what is otherwise a deferred job pipeline)

Any RIMAPI code that calls pathfinding **must** use `#if RIMWORLD_1_5` / `#elif RIMWORLD_1_6`
conditional compilation, exactly as `MapHelper.cs` already does for `AllRooms`.

### 1.6 `RegionGrid.AllRooms` type change

- 1.5: `List<Room> allRooms` (public field, lowercase)
- 1.6: `IReadOnlyList<Room> AllRooms` (property, PascalCase)

Pattern already in use in `MapHelper.cs` at lines 396 and 420.

### BFS `regionProcessor` called for `startReg`

`RegionProcessor` is called for the start region before any neighbors are enqueued.
If `startReg == endReg`, the processor sees it immediately and `found` becomes `true`
with `regionsTraversed = 1`. Short-circuit before calling `BreadthFirstTraverse` when
`startReg == endReg` if you want cost 0 for same-region pairs.

---

## Callsite index

| API | File | Line | Form |
|---|---|---|---|
| `pawn.CanReach` | `C:\dev\RimMind\Source\RimMind\Tools\DraftedPawnTools.cs` | 45 | `CanReach(dest, PathEndMode.OnCell, Danger.Deadly)` |
| `map.reachability.CanReachColony` | `C:\dev\RimMind\Source\RimMind\Tools\WorkTools.cs` | 735 | `CanReachColony(thing.Position)` |
| `map.reachability.CanReachColony` | `C:\dev\RimMind\Source\RimMind\Tools\WorkTools.cs` | 740 | `CanReachColony(designation.target.Cell)` |
| `map.regionGrid.allRooms` (1.5) | `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Helpers\MapHelper.cs` | 396 | `map.regionGrid.allRooms` |
| `map.regionGrid.AllRooms` (1.6) | `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Helpers\MapHelper.cs` | 420 | `map.regionGrid.AllRooms` |
| `map.regionGrid.AllRooms` | `C:\dev\RimMind\Source\RimMind\Tools\ColonistTools.cs` | 419 | `map.regionGrid.AllRooms` |
| `map.regionGrid.AllRegions` | `C:\dev\RimMind\Source\RimMind\Tools\ColonyTools.cs` | 168 | `map.regionGrid.AllRegions` |
| `map.regionGrid.AllRooms` | `C:\dev\RimMind\Source\RimMind\Tools\EnvironmentTools.cs` | 19 | `map.regionGrid.AllRooms` |
| `map.regionGrid.AllRooms` | `C:\dev\RimMind\Source\RimMind\Tools\SemanticTools.cs` | 54 | `map.regionGrid.AllRooms` |
| Type stubs (all listed APIs) | Krafs.Rimworld.Ref 1.5.4104 | — | `Assembly-CSharp.dll` MetadataReader |
| Type stubs (all listed APIs) | Krafs.Rimworld.Ref 1.6.4518 | — | `Assembly-CSharp.dll` MetadataReader |

---

## Plan corrections vs. rimapi-map-reach-and-path-cost.md

The plan was written from memory. Verified discrepancies:

| Claim in plan | Actual |
|---|---|
| `Map.pathFinder.FindPath(from, to, traverseParms, peMode)` | Correct for **1.5** only; in 1.6 it's `FindPathNow(from, to, traverseParms, null, peMode)` with different param order |
| `path == PawnPath.NotFound` | Correct — `NotFound` is a static property on `PawnPath` that returns a sentinel instance |
| `(int)path.TotalCost` | Correct — `TotalCost` is `float`, explicit cast to `int` is fine |
| `using var path = ...` — disposes via pool | Correct — `PawnPath.Dispose()` confirmed present in both versions |
| `map.regionGrid.GetValidRegionAt_NoRebuild(from)` | Correct — method present in both 1.5 and 1.6 |
| `reg => toReg.Allows(traverseParms, false)` in `RegionEntryPredicate` | Minor: predicate receives `(from, to)` pair; should be `(fromReg, toReg) => toReg.Allows(...)` |
| `TraverseParms.For(TraverseMode.<mode>)` | Correct — implicit conversion from `TraverseMode` exists; the `For(TraverseMode, Danger, ...)` factory is the fuller form |
| `PathEndMode.ClosestTouch` (plan says `closest_touch`) | Correct mapping — enum value is `ClosestTouch = 3` |

The plan's implementation sketches are valid for 1.5. Add `#if RIMWORLD_1_5 / #elif RIMWORLD_1_6`
guards around the `FindPath`/`FindPathNow` call site, exactly as `MapHelper.cs` already
patterns for `AllRooms`.
