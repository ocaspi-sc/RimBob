# ColonyContext Retirement

Resolves: `colonycontext-retirement` in Tasks.md

## Problem

`ColonyContext` is a phantom parameter. Every call site passes `ColonyContext.Default`; no `Evaluate` implementation reads any field from it (Mayor's `MayorAgendaRules` names the param `_`). The context predates `PlayCycleContext` and agenda-derived briefings, which now carry the real colony posture signal. Keeping the param is noise.

## Scope

**Interface**
- `Src/Common/Ministers/IMinisterRules.cs` — drop `ColonyContext context` from `Evaluate`

**Rule implementations** (drop param from all signatures)
- `Src/Ministers/Food/Rules.cs`
- `Src/Ministers/Welfare/Rules.cs`
- `Src/Ministers/Willie/Rules.cs` — two overloads; inner overload also drops `inboundBuildingRequests` after the context param
- `Src/Ministers/Mayor/MayorAgendaRules.cs` — drop `ColonyContext _` param (Mayor has its own rules interface; check for `IMayorAgendaRules` and update it too)

**Callers** (drop `ColonyContext.Default` argument)
- `Src/Ministers/Food/Chef.cs`
- `Src/Ministers/Welfare/MinisterOfWelfare.cs`
- `Src/Ministers/Willie/MinisterOfWillie.cs`
- `Src/Ministers/Mayor/Mayor.cs`
- `Src/ApiHost/AgendaBootstrapHostedService.cs`

**Tests** (drop `ColonyContext.Default` argument, ~60 call sites)
- `Src/Tests/Food/FoodRulesTests.cs` (~45 calls)
- `Src/Tests/Willie/WillieRulesTests.cs` (~8 calls — some pass context alone, some also pass `inboundBuildingRequests`)
- `Src/Tests/Welfare/WelfareRulesTests.cs` (1 call)
- `Src/Tests/Mayor/MayorAgendaRulesTests.cs` (7 calls)
- `Src/Tests/Food/FoodChainModelBuilderTests.cs` (1 call)
- `Src/Tests/Food/FoodReplayCorpusTests.cs` (1 call)

**Delete**
- `Src/Common/Ministers/ColonyContext.cs` — nothing imports it after the above

## Approach

Mechanical find-and-replace. No logic changes. For Willie's two-overload case:

Before:
```csharp
public RuleRun Evaluate(WillieBriefing briefing, ColonyContext context) =>
    Evaluate(briefing, context, []);

public RuleRun Evaluate(
    WillieBriefing briefing,
    ColonyContext context,
    IReadOnlyList<BuildingRequest> inboundBuildingRequests)
{ ... }
```

After:
```csharp
public RuleRun Evaluate(WillieBriefing briefing) =>
    Evaluate(briefing, []);

public RuleRun Evaluate(
    WillieBriefing briefing,
    IReadOnlyList<BuildingRequest> inboundBuildingRequests)
{ ... }
```

`MinisterOfWillie.cs` call: `rules.Evaluate(briefing, ColonyContext.Default, inboundRequests)` → `rules.Evaluate(briefing, inboundRequests)`.

Test calls in WillieRulesTests that pass only `(briefing, ColonyContext.Default)` → `(briefing)`. Calls that pass `(briefing, ColonyContext.Default, requests)` → `(briefing, requests)`.

## Verification

- `dotnet build Src/RimBob.sln` green
- `dotnet test Src/Tests/RimBob.Tests.csproj` green

## No compat

No wire/persistence change. No wipe-and-regen needed. Pure interface cleanup.
