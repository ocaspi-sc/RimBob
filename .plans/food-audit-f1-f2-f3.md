# Food Audit — Apply Findings F1 + F2 + F3

From the [2026-05-20 minister-refine audit](../HumanTodo.md).

## Scope

Three small, independent changes. No runtime behavior changes; no briefing changes.

---

## F1 — Seed replay corpus JSONL

**File:** `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl`

`FoodReplayCorpusTests.HistoricalRuleReplayCorpus_RerunsWithoutLlmAndMatchesStableOutput` asserts `cases.Should().NotBeEmpty()`. The file is empty or missing, so the test is a latent failure.

Seed with at least two rule-path replay records:

1. `emergency_food_flag` — a briefing with `EstimatedDaysOfFood ≈ 4`, standard colonist count, raw food and meals present, growing season open. Expected: one `food_security` High advice, one flag.
2. `harvest_mature_crops` — a briefing with `ReadyToHarvest > 0` and `EstimatedDaysOfFood ≈ 18`. Expected: one `harvest_now` Medium advice, no flag.

Each JSONL line must include: `minister`, `path`, `source_id`, `rule_trace`, `briefing` (full `FoodBriefing` JSON), `advice` (array), `flags` (array).

Use `FoodRulesTests.Briefing(days)` as the briefing template; serialize to JSON via `System.Text.Json` with `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`.

**Verification:** `dotnet test --filter FoodReplayCorpusTests` passes with at least 2 cases evaluated.

---

## F2 — Remove `state_summary` from LLM output schema

**File:** `Src/LlmGateway/prompts/food.system.md`

`MinisterOfFood.RunEscalationAsync` calls `FoodStateSummary.Build(briefing)` and passes that to `PublishSnapshot` — the LLM's `state_summary` field is never read. The system prompt currently instructs the LLM to emit it and explains how to populate it, wasting tokens every escalation.

Changes:
1. Remove `"state_summary": "..."` from the JSON schema block at the top of the prompt.
2. Remove the rule `"Always emit state_summary before advice. It is a high-level current-state bullet list..."`.
3. Remove any remaining instruction referencing `state_summary`.

Do not change anything else in the prompt.

**Verification:** `FoodPromptTests` passes. Spot-check: the JSON schema in the prompt no longer contains `state_summary`.

---

## F3 — Add `freezer_missing` unit test

**File:** `Src/Tests/Food/FoodRulesTests.cs`

No test covers the `freezer_missing` rule (`Coolers == 0 && days >= 20 && FoodUnits > 0`).

Add one test:

```csharp
[Fact]
public void NoCoolerWithStableBuffer_EmitsFreezerMissingAdvice()
{
    FoodBriefing briefing = Briefing(days: 25f) with
    {
        Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1)
    };

    Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
        .Should().BeOfType<Decision>().Subject;

    decision.Trace.Should().Be("freezer_missing");
    AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
    advice.AdviceType.Should().Be("manage_freezer");
    advice.Priority.Should().Be(AdvicePriority.Medium);
    advice.Actions.Should().ContainSingle().Which.Kind.Should().Be(AdviceActionKind.PlaceBlueprint);
    advice.Actions.Single().Owner.Should().Be("Construction");
    decision.Flags.Should().ContainSingle().Which.Requests.Should()
        .Contain(r => r.Kind == ResourceRequestKind.Building && r.RequestedFrom == "Construction");
}
```

**Verification:** `dotnet test --filter FoodRulesTests` passes including the new test.

---

## Order

Apply in any order; all three are independent. F3 is fastest. F2 is one-file prompt edit. F1 requires serializing two briefing snapshots.

## Verification (full)

```
dotnet test Src/RimBob.sln --filter "FullyQualifiedName~RimBob.Tests.Food"
```
