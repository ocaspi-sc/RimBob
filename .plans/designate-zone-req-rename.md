# Rename Advice Action `designate_zone` -> `designate_zone_req`

**Status:** PLANNED - design only; do not implement until the user says to execute it.
**Owner:** Chef + Willie + Dashboard.
**Scope:** Rename the single advice-action token `designate_zone` -> `designate_zone_req` (the `AdviceActionKind.DesignateZone` member and its wire string). Mechanical, no behavior change. No-compat: regenerate persisted outputs.

---

## Goal

The grow-zone work left one deferred item: Chef's player-facing `designate_zone` action is a *request stub* (it tells the player to make a zone, pending a concrete Willie-placed option), not a placed-zone write. The parent plan's Locked Decision said: if that token is renamed to `designate_zone_req`, do it as its own no-compat wire slice. This is that slice.

**In scope:** the `AdviceActionKind` member + its wire token only. **Not** the `ZoneRequest` / `zone_requests[]` / `ZoneClass` request family, and **not** the `create_growing_zone` Apply kind — both stay exactly as named.

---

## Locked Decisions

- Rename the C# enum member `AdviceActionKind.DesignateZone` -> `DesignateZoneReq`. The enum carries `[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceActionKind>))]` (`Src/Common/Advice/AdviceAction.cs:189`), so the wire string auto-derives to `designate_zone_req` — no attribute override needed.
- Behavior-neutral: the action still emits from the same rules with the same instruction text, owner, work type, and skill. Only the kind token changes.
- No-compat: wipe-and-regen persisted minister snapshots and replay corpus records that contain `designate_zone`. Do not add a legacy reader/alias.
- Do not touch `ZoneRequest`/`zone_requests[]`/`ZoneClass` or `create_growing_zone`.

---

## Touchpoints

**C# — auto-updated by the member rename (compile-time refs):**
- Definition: `Src/Common/Advice/AdviceAction.cs:192` (`DesignateZone,` -> `DesignateZoneReq,`).
- Emitters: `Src/Ministers/Food/Rules.cs:984` (Chef `GrowingZoneAction`), `Src/Ministers/Willie/MinisterOfWillie.cs:590`, `Src/Ministers/Willie/Rules.cs:251`.
- Consumers: `Src/LlmGateway/AdviceActionNormalizer.cs:63`, `Src/Common/Briefings/FoodChainModelBuilder.cs:305`.
- Test refs (symbol auto-updates): `Src/Tests/Willie/MinisterOfWillieTests.cs:203,232`, `Src/Tests/Food/FoodRulesTests.cs:117,157,747,850`, `Src/Tests/ApiHost/AssistedApplyServiceTests.cs:940`, `Src/Tests/Coordination/AdviceActionApplySerializationTests.cs:57`.

**Manual string-literal updates (`designate_zone` -> `designate_zone_req`):**
- Dashboard: `Dashboard/src/dashboard/semanticIcons.ts:274` (map key), `Dashboard/src/components/minister/RuleCard.tsx:358`, `Dashboard/src/components/home/HomeOverview.tsx:1137`. (Note: `action.kind` in `Dashboard/src/types/advice.ts` is a generic `string`, not a union — no type change needed.)
- Prompt: `Src/LlmGateway/prompts/food.system.md:18` (action-kind list).
- Docs: `Docs/DESIGN.md:200`, `Docs/design/advice.md:77,87`.
- Any literal `"designate_zone"` assertion in the serialization tests (`AdviceActionApplySerializationTests`, `MinisterOfWillieTests`) -> `designate_zone_req`.

**Regenerate (no-compat):**
- `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl` (contains `designate_zone`).
- Any persisted minister snapshot carrying `designate_zone`.

---

## Verification

- `dotnet test Src\RimBob.sln --filter "FullyQualifiedName~Food|FullyQualifiedName~Willie|FullyQualifiedName~Coordination|FullyQualifiedName~ApiHost"`, then full `dotnet test Src\RimBob.sln` before landing.
- `npm.cmd run build` after dashboard string changes.
- Grep the repo for any remaining `designate_zone` (without `_req`) outside `.plans/` history — expect zero in code/prompts/docs/fixtures.
- Regenerate replay corpus; confirm the regenerated rows carry `designate_zone_req` and no `designate_zone`.

---

## Non-Goals

- No rename of `ZoneRequest` / `zone_requests[]` / `ZoneClass`.
- No rename of the `create_growing_zone` Apply kind.
- No behavior change to the rules that emit the action.
- No legacy alias for the old token.

---

## Summary — landed `dfc1336`

Mechanical, behavior-neutral rename of the one advice-action token `designate_zone` -> `designate_zone_req`, shipped via gimp (Codex run `20260608-002216-designate-zone-req-rename`).

**What shipped.** Enum member `AdviceActionKind.DesignateZone` -> `DesignateZoneReq` (`Src/Common/Advice/AdviceAction.cs`); wire string auto-derived to `designate_zone_req` via `SnakeCaseLowerEnumConverter`. All compile-time refs updated (3 emitters: Chef `Rules.cs`, Willie `MinisterOfWillie.cs` + `Rules.cs`; 2 consumers: `AdviceActionNormalizer.cs`, `FoodChainModelBuilder.cs`). Manual string updates in 3 dashboard files, the food prompt, and 2 docs. Replay-corpus fixture regenerated. 17 files total. `ZoneRequest`/`zone_requests[]`/`ZoneClass` and `create_growing_zone` untouched, per scope.

**Verification.** `dotnet test --filter "kind!=reach"` = 634 passed / 0 failed. Full run's only reds are the 5 documented `AdviceForNewColony1Tests` `kind=reach` targets (pre-existing, unrelated). Grep proof: zero `designate_zone` without `_req` / `DesignateZone` without `Req` outside `.plans/`. Verifier (Sonnet) returned Adherent: yes, no scope drift.

**How to verify (human).**
  - Code: `git -C C:\dev\RimBob grep -n "DesignateZoneReq" Src/Common/Advice/AdviceAction.cs`.
  - Dashboard: a Chef grow-zone advice card still renders its crop/zone icon (semanticIcons key `designate_zone_req`).
  - Live: run a cabinet cycle; Chef `expand_growing_capacity` advice carries an action with `"kind":"designate_zone_req"`.

**Codex run:** `20260608-002216-designate-zone-req-rename` · landed commit `dfc1336`.

**Deferred (post-land ops):** AppData persisted minister snapshots / replay logs still hold the old token — Codex skipped the wipe because live Hosts were running from other checkouts. Regenerated on the next fresh Host boot (folds into live proof); the committed test fixture is already regenerated.
