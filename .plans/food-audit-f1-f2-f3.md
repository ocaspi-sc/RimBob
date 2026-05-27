# Food Audit — Apply Findings F1 + F2 + F3

From the [2026-05-20 minister-refine audit](../HumanTodo.md).

## Scope

Three small, independent changes. No briefing changes. **One intentional runtime behavior fix** (F3 — see below).

---

## F1 — Seed replay corpus JSONL

**File:** `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl`

`FoodReplayCorpusTests.HistoricalRuleReplayCorpus_RerunsWithoutLlmAndMatchesStableOutput` asserts `cases.Should().NotBeEmpty()`. The file is empty or missing, so the test is a latent failure.

Seed with at least two rule-path replay records:

1. `emergency_food_flag` — a briefing with `EstimatedDaysOfFood ≈ 4`, standard colonist count, raw food and meals present, growing season open. Expected: one `food_security` High advice, one flag.
2. `harvest_mature_crops` — a briefing with `ReadyToHarvest > 0` and `EstimatedDaysOfFood ≈ 18`. Expected: one `harvest_now` Medium advice, no flag.

Each JSONL line must include: `minister`, `path`, `source_id`, `rule_trace`, `briefing` (full `FoodBriefing` JSON), `advice` (array), `flags` (array).

Use `FoodRulesTests.Briefing(days)` as the briefing template; serialize to JSON via `System.Text.Json` with `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`.

**Also fix `FoodReplayCorpusTests.cs` line 85:** change `actualItem.Actions.Should().Equal(expectedItem.Actions)` to `actualItem.Actions.Should().BeEquivalentTo(expectedItem.Actions)`. `Equal` uses reference equality; record types with `List<string>` fields (e.g. `AdviceActionApply.TargetIds`) will always fail even when contents match. `BeEquivalentTo` does deep structural comparison and is correct here.

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

**Also fix `FoodPromptTests.cs` lines 67-69:** remove these three assertions (they test for `state_summary` which is being deleted):
```csharp
builder.FoodSystemPrompt.Should().Contain("\"state_summary\"");
builder.FoodSystemPrompt.Should().Contain("Always emit state_summary before advice");
builder.FoodSystemPrompt.Should().Contain("high-level current-state bullet list");
```
Leave all other assertions in that test intact.

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
    advice.Actions.Single().Owner.Should().Be("Willie");
    decision.Flags.Should().ContainSingle().Which.Requests.Should()
        .Contain(r => r.Kind == ResourceRequestKind.Building && r.RequestedFrom == "Willie");
}
```

**Also fix `Rules.cs` line 156:** change `false` → `true` (the `emitFlag` parameter) in the `freezer_missing` `DecisionFor` call. The rule already defines a `ResourceRequest` (Building → Willie) but `emitFlag = false` silently drops it — no flag is ever emitted. This is a bug: the request is defined but never sent. Flipping to `true` makes the rule emit the flag as intended. This is a runtime behavior change (flag now emitted where it wasn't), which is the one exception to the "no runtime behavior changes" constraint for this plan.

**Verification:** `dotnet test --filter FoodRulesTests` passes including the new test.

---

## Order

Apply in any order; all three are independent. F3 is fastest. F2 is one-file prompt edit. F1 requires serializing two briefing snapshots.

---

## Summary (landed 2026-05-21)

**Motivation.** Three latent defects found during minister-refine audit of the Food minister: an empty replay corpus making a corpus test vacuous, a wasted `state_summary` field wasting tokens every escalation, and an untested `freezer_missing` rule that also had a dormant flag bug.

**Context.** Food minister rules + LLM gateway prompt + test suite. No briefing schema changes. One runtime behavior fix (F3 flag emission).

**Scope.**
- F1: Seeded `food-rules-history.jsonl` with `emergency_food_flag` and `harvest_mature_crops` cases; fixed `FoodReplayCorpusTests.cs` line 85 (`Equal` → `BeEquivalentTo`)
- F2: Removed `state_summary` from `food.system.md` JSON schema and rules; dropped 3 matching assertions from `FoodPromptTests.cs`
- F3: Added `NoCoolerWithStableBuffer_EmitsFreezerMissingAdvice` test; fixed `Rules.cs` line 156 `emitFlag false → true` (flag was defined but silently dropped)

**How to verify (human).**
- Commands: `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~RimBob.Tests.Food"` — 82 tests pass
- Files: `food.system.md`, `food-rules-history.jsonl`, `FoodRulesTests.cs`, `FoodPromptTests.cs`, `FoodReplayCorpusTests.cs`, `Rules.cs`
- No dashboard surface for these changes (rules/test/prompt only)

**Codex run:** 20260521-123056-food-audit-f1-f2-f3 · branch `codex/prompt-20260521-123056-food-audit-f1-f2-f3` · landed commit `068b44b9`

## Verification (full)

```
dotnet test Src/RimBob.sln --filter "FullyQualifiedName~RimBob.Tests.Food"
```
