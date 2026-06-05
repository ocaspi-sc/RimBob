# Chef Bootstrap Removal

Resolves: `verify-first-cycle-bootstrap-removed` in Tasks.md

## Problem

`Chef.cs:47-65` has a legacy `if (cycle.IsBootstrap)` branch that forces a first-cycle LLM escalation.
`Docs/design/ministers.md` dropped the "First Live Cycle Bootstrap" rule: rules+escalation run identically on every cycle; `StartupBootstrap` is a wake *reason* only, not a policy gate.
Welfare already removed its equivalent (test `StartupBootstrap_DoesNotForceLlm` confirms).

## Scope

`Src/Ministers/Food/Chef.cs` — delete the IsBootstrap block (currently lines 47-65).

`Src/Tests/Food/FoodMinisterTests.cs` — review for any test asserting LLM was called on bootstrap. If found, remove or rewrite as a normal rules-path assertion.

No other files. `PlayCycleContext.StartupBootstrap` stays (valid wake reason used in tests). `ColonyContext` is untouched (separate plan).

## Change

Delete:

```csharp
if (cycle.IsBootstrap)
{
    log.LogInformation("Chef bootstrap: forcing first live cycle escalation");
    bool bootstrapped = await RunEscalationAsync(
        cycle,
        briefing,
        context,
        new Escalate(
            "bootstrap_first_live_cycle",
            "first live Chef cycle forces an LLM bootstrap memo",
            new { briefing.BriefingVersion, briefing.GameTick }),
        RuleTraceDetails.Escalated(
            "bootstrap_first_live_cycle",
            "first live Chef cycle forces an LLM bootstrap memo"),
        ct);
    if (bootstrapped) return;

    log.LogWarning("Chef bootstrap escalation failed; falling back to normal rules evaluation.");
}
```

After deletion, `cycle.IsBootstrap` is still used in no remaining Chef code — no other references needed.

## Verification

- `dotnet build Src/RimBob.sln` green
- `dotnet test Src/Tests/RimBob.Tests.csproj` green
- No behavior change on normal cycles. On `StartupBootstrap` trigger, Chef now runs rules (not forced LLM) — same as Welfare.

## No compat

No wire/persistence change. No wipe-and-regen needed.

---

## Summary (landed 2026-06-05)

**Motivation.** `Docs/design/ministers.md` dropped the "First Live Cycle Bootstrap" rule — rules+escalation run identically on every cycle; `StartupBootstrap` is a wake reason only. Welfare already removed its equivalent (test `StartupBootstrap_DoesNotForceLlm` confirms). Chef was the last feeder still forcing LLM on the first cycle.

**Scope.**
- `Chef.cs` — deleted the `if (cycle.IsBootstrap)` block (19 lines)
- `FoodMinisterTests.cs` — rewrote the three bootstrap-forcing tests to assert rules-first behavior and zero LLM calls; renamed a fourth test to drop "Bootstrap" semantics

**How to verify (human).**
- Restart RimBob; on the first cabinet wake after startup, Chef runs rules (not LLM). No "Chef bootstrap: forcing first live cycle escalation" log line.
- `dotnet test Src/Tests/RimBob.Tests.csproj` → 575 pass, 0 fail.

**Deferred.** `Docs/design/ministers/food.md` still has stale bootstrap wording — separate doc-only cleanup.

**Codex run:** 20260605-150901-chef-bootstrap-removal · branch `codex/prompt-20260605-150901-chef-bootstrap-removal` · landed commit `cdcae7dc2eba74a19d38a66828b7c46c88f46cdf`
