# null-Stamp crash fix — IsCompleteStrictAdvice

## Problem

`POST /api/ministers/{minister}/llm-output/manual` with no `stamp` field:

1. `TryDeserialize<AdviceItem>` succeeds → `Stamp = null`
2. `IsCompleteStrictAdvice` passes (only checks `Id` + `Actions[].Instruction`)
3. `NormalizeStrictAdvice` returns item with `Stamp = null`
4. `AdviceBus.ReplaceMinisterAdvice` → `SortAdvice` → `.ThenByDescending(a => a.IssuedAt)`
5. `IssuedAt` = `Stamp.IssuedAt` → `NullReferenceException` → 500

With 1 item, .NET 9 skips `ThenBy` key eval (single-element sort). With 2+ items, throws.

## Fix

`Src/LlmGateway/AdviceResponseNormalizer.cs` line 216 — add `advice.Stamp is not null` guard:

```csharp
// Before
private static bool IsCompleteStrictAdvice(AdviceItem advice) =>
    !string.IsNullOrWhiteSpace(advice.Id) &&
    advice.Actions.All(action =>
        !string.IsNullOrWhiteSpace(action.Instruction));

// After
private static bool IsCompleteStrictAdvice(AdviceItem advice) =>
    !string.IsNullOrWhiteSpace(advice.Id) &&
    advice.Stamp is not null &&
    advice.Actions.All(action =>
        !string.IsNullOrWhiteSpace(action.Instruction));
```

Items without `stamp` fall through to the fallback normalization path, which creates a proper `AdviceStamp` with `DateTimeOffset.UtcNow` timestamps.

## Scope

- 1 file, 1 line inserted: `Src/LlmGateway/AdviceResponseNormalizer.cs`
- No wire/corpus/test changes required (existing tests don't exercise null-stamp path)

## Verification

Post a manual LLM output with 2+ advice items and no `stamp` field → expect 200, not 500.
