# Welfare — LLM Slice C (implementation plan)

> **Updated 2026-06-03** for the post-refactor architecture: no `concern` (deleted), table-driven all-hits rules, `RuleRun` + `Decision` model where **`Escalate` is a `Decision` subtype**. See [`welfare-meta-plan.md`](welfare-meta-plan.md) §0.
>
> Give Welfare an **LLM escalation path** for Mood & Needs judgment calls rules should not hard-code (social fights, ideology, temperature/apparel trade-offs, specific-pawn intervention, Welfare/Medical edge). Promotes Welfare to a full feeder at parity with Chef. Mirrors `Src/Ministers/Food/Chef.cs` **minus the first-cycle bootstrap** (removed from the contract — `Docs/design/ministers.md` "First Live Cycle").
>
> **Builds on** landed Slices A (`eee91c1`) + B (`319930f`) + table port (`b8189f3`). `Suggest`-mode. No Welfare-owned Apply (Willie owns build Apply). Every LLM attempt is replayable.

---

## 1. Grounding — the new escalation seam (verified)

- `Welfare/Rules.Evaluate` returns a `RuleRun(decisions, diagnostics)`. The rule table runs all-hits via `MinisterRuleTableEvaluator.EvaluateAllHits`; when no rule matches it falls through to an empty `needs_stable` `RuleRun`. **Welfare never escalates today.**
- **`Escalate` is a `Decision`** (`Src/Common/Ministers/Decision.cs`): `Escalate(RuleId Rule, string Reason, object? Context, …)`. A minister escalates by including an `Escalate` decision in the `RuleRun`.
- **Chef's seam (post-refactor, the mirror):** `Escalate? escalation = result.Decisions.OfType<Escalate>().SingleOrDefault();` then, unless `RunMode.RulesOnly`, `await RunEscalationAsync(cycle, briefing, context, escalation, result.Diagnostics, ct)`. `RunMode.ForceLlm` builds an `Escalate` directly; bootstrap also builds one (**Welfare omits bootstrap**). `RuleTraceDetails.Escalated(rule, reason)` tags the trace.
- **Shared LLM infra to reuse:** `LlmClient` (`Src/LlmGateway/`), `AdviceResponseNormalizer` / `AdviceActionNormalizer` / `ResourceRequestNormalizer`, `RawLlmOutputStore`, `RagRetriever` + per-minister retriever/query-builder (Food/Mayor pattern). Prompt: `Src/LlmGateway/prompts/food.system.md` → add `welfare.system.md`.
- **No concern anywhere.** The LLM emits advice fields + flags; the normalizer wraps them into `Advise` + `Request*` decisions keyed by a trace id. There is no `allowed_concerns` contract and no `WelfareConcern` to map.
- Manual-LLM ingestion (`/api/ministers/{minister}/llm-output/manual`) + raw-output capture are generic; they activate for Welfare via the registry flags (WC6).

**Bootstrap note:** `Chef.cs` still builds a bootstrap `Escalate` ("forcing first live cycle escalation"). Welfare must **not** add that arm. (Cross-ref `verify-first-cycle-bootstrap-removed` — Chef's is the instance to delete.)

---

## 2. No concern — what the LLM emits

The escalation-heavy cases are *why* Welfare needs an LLM, but there is **no concern enum to fill**. The LLM produces ordinary advice fields (priority, title, body, rationale, `actions[]`) plus optional cross-minister flags; the normalizer wraps each into an `Advise` decision (trace id `welfare_llm`, or the escalation rule id) and `RequestBuild`/`RequestItem`/`RequestAttention` decisions. Welfare actions stay `note`; the LLM never authors `place_blueprint` (Willie owns placement) — strip any it emits. Out-of-scope drivers (social/ideology/temperature) are named in body/rationale and routed via flags to the owning minister; they do not need a taxonomy slot in the output.

Promoting recurring categories to **deterministic table rules** (`social_pressure`, `temperature_comfort`, …) is the evidence-driven Slice D, informed by what the LLM keeps emitting here.

---

## 3. Steps

### WC1 — Escalation decision in `Rules.cs`

Escalation is the **fallthrough**, mutually exclusive with the wired rules (so `OfType<Escalate>().SingleOrDefault()` holds). Edit the existing tail of `Evaluate`:

```
RuleRun ruleRun = MinisterRuleTableEvaluator.EvaluateAllHits(rules, briefing, additionalRuleDescriptors: fallback);
if (ruleRun.Decisions.Count > 0) return ruleRun;          // a wired rule fired → deterministic, no LLM
if (HasUnexplainedMoodPressure(briefing))                  // NEW
    return new RuleRun(
        [ new Escalate("unexplained_mood_pressure", UnexplainedReason(briefing), BuildEscalationContext(briefing)) ],
        DiagnosticsForFallback(rules, fallback, briefing, "unexplained_mood_pressure"));
return new RuleRun([], DiagnosticsForFallback(rules, fallback, briefing, "needs_stable"));   // calm → silent
```

- `HasUnexplainedMoodPressure` = no wired rule matched **and** material pressure remains: a dominant `ThoughtDigest` category **outside** `{ShelterSleep, Recreation, ComfortBeauty}` (i.e. `Social`, `Ideology`/`Other`, `Temperature`, `Health`) with material offset, **or** `Mood.AverageMood` below a tunable escalation floor. Calm colony → `needs_stable` (no escalate).
- `BuildEscalationContext` = compact evidence (dominant categories from `ThoughtDigest`, worst pawns + drivers, mood distribution) — same compact discipline as Chef.
- Add the `unexplained_mood_pressure` row to the fallback rule descriptors so the trace shows the escalate decision.

### WC2 — `welfare.system.md` system prompt

New `Src/LlmGateway/prompts/welfare.system.md` (mirror `food.system.md` structure; ensure it copies to build-output `prompts/`). No concern vocabulary. Sections:
- **Role/domain:** Welfare owns **Mood & Needs**. Near-term, actionable, sparse advice.
- **Output contract:** required `priority`; `title`; `body`; `rationale`; concrete `actions[]` (Welfare uses `note`; never `place_blueprint`); optional `flags` with typed `building_requests` (`requested_from:"Willie"` for builds, `item_requests`/`attention` for Chef/player). Match the fields the runtime normalizer (WC4) accepts. **No `allowed_concerns`.**
- **Hand-off rules:** hunger→Chef, treatment→Medical (future; player note until live), build/room→Willie, apparel→Industry (future), guest trade→Economy (future). Welfare keeps the mood/break-risk framing; route only to live owners (Chef, Willie).
- **Grounding:** cite guide snippets when they change the call. Keep tight (briefing is the quality lever).

### WC3 — LLM wiring in `MinisterOfWelfare`

Add `LlmClient llm` + `WelfareRagRetriever retriever` ctor deps. Mirror Chef's current `RunPlayCycle` **minus the `IsBootstrap` block**:
- `RunMode.ForceLlm` → build an `Escalate("manual_llm_trigger", …)` and `RunEscalationAsync`.
- `result = rules.Evaluate(...)`; `Escalate? escalation = result.Decisions.OfType<Escalate>().SingleOrDefault();`
  - `escalation is not null && RunMode != RulesOnly` → `await RunEscalationAsync(cycle, briefing, context, escalation, result.Diagnostics, ct)`.
  - `escalation is not null && RunMode == RulesOnly` → publish empty snapshot + replay (`EscalationReason`/`EscalationContext`, no LLM) and stop.
  - else → publish the `Decision` snapshot + replay (existing landed path).
- `RunEscalationAsync`: retrieve RAG, call `llm`, parse+normalize (WC4) into decisions, `PublishSnapshot`, `PersistReplayAsync` with `EscalationReason`/`EscalationContext`/`GuideCitations`/normalized output; on failure log + keep prior snapshot, return bool. **No bootstrap arm.**

### WC4 — Welfare LLM response parse + normalize

New `WelfareLlmResponseParser` (mirror `FoodLlmResponseParser`) → typed `WelfareLlmResponse`, reusing shared `AdviceResponseNormalizer` / `AdviceActionNormalizer` / `ResourceRequestNormalizer`. It maps the model output into `Decision`s:
- One `Advise(trace:"welfare_llm", priority, title, body, rationale, actions)` per advice item; require `priority` (default+log if missing).
- Normalize `actions[]` to Welfare-legal kinds (`note`); **strip** any model-authored `place_blueprint`/Apply handle.
- Map flags → `RequestBuild`(`RequestedFrom:"Willie"`) / `RequestItem` / `RequestAttention`; drop malformed flags with a surfaced count (avoid the silent-drop gap noted for Chef's `NormalizeFlag`).
- No concern field to parse or repair.

### WC5 — Welfare RAG retrieval

New `WelfareRagRetriever` + `WelfareRagQueryBuilder` (mirror the Food/Mayor pair over shared `RagRetriever`; or a Welfare profile in the generalized retrieval flow). Query the mood/room/recreation corpus: `Docs/guides/Base/rimworldwiki-rooms.md`, `Docs/guides/beginner/{survival-tactics,beginner-survival-tips,tips-and-tricks,wealth-management}.md`. Build queries from the escalation context (dominant driver category + worst pawns + room quality).

### WC6 — Registry flip + DI

- `MinisterRegistry.cs`: Welfare descriptor → `EnabledViews: MinisterViews` (adds `prompt`/`rag`/`raw_llm`), `CanRunLlm: true`, `HasPrompt: true`, `HasRag: true`, `HasRawLlmOutput: true`, `HasManualLlmOutput: true` (keep `Ready:true`, `CanRunRules:true`, `CanManualTrigger:true`, `CabinetOrder:12`).
- `Program.cs`: register `WelfareRagRetriever` + query builder; inject `LlmClient` + retriever into `MinisterOfWelfare` (mirror Chef). Confirm the manual-LLM ingestion endpoint + raw-output store resolve for `welfare`.

### WC7 — Tests + fixtures

- **Rules escalation:** `social-fight-dominant` (Social thought dominates, no wired rule) → `RuleRun` with one `Escalate("unexplained_mood_pressure")`; `calm` → empty `needs_stable`, no escalate; `wired-rule-present` (shelter gap + stray social thought) → decisions present, **no** `Escalate`.
- **Response normalization:** valid response → `Advise` decisions; flag → `RequestBuild` with `RequestedFrom:"Willie"`; malformed flag dropped + counted; model `place_blueprint` stripped; missing priority defaulted.
- **Minister:** `ForceLlm` runs escalation; `RulesOnly` stops before LLM (publishes empty); escalation failure keeps prior snapshot; replay carries escalation context + guide citations + normalized output. Use a fake `LlmClient` (existing LLM tests) or the `run-minister-using-subagent` skill when Gemini is unavailable.
- **No bootstrap:** assert Welfare does not escalate on a first/`IsBootstrap` cycle.

---

## 4. Acceptance

- `dotnet test Src/Tests/RimBob.Tests.csproj` green incl. new escalation/normalization fixtures.
- Material unexplained mood pressure → one `Escalate` decision → LLM advice (`Advise`/`Request*`, correct hand-off flags); calm colony silent; wired-rule colony stays deterministic (no LLM).
- `ForceLlm`/`RulesOnly` behave like Chef; **no** first-cycle bootstrap escalation.
- Every escalation replayable (briefing + context + guide citations + normalized output + failure detail).
- Dashboard Welfare exposes prompt / rag / raw_llm views; manual-LLM ingestion works for `welfare`.

---

## 5. Out of scope

- Deterministic `social_pressure` / `temperature_comfort` / `guest_hospitality` / `animal_welfare` / `schedule_balance` table rules + their briefing signals — evidence-driven Slice D.
- Welfare-owned Assisted Apply (none).
- Pushback/Oracle refinement wiring (repo-wide M5/M6).
- Welfare/Medical sequencing decision (meta-plan §7).
- Deleting Chef's bootstrap (tracked by `verify-first-cycle-bootstrap-removed`).

---

## 6. Files

**New:** `Src/LlmGateway/prompts/welfare.system.md`, `Src/LlmGateway/WelfareLlmResponseParser.cs` (+ `WelfareLlmResponse`), `Src/KnowledgeBase/WelfareRagRetriever.cs`, `Src/KnowledgeBase/WelfareRagQueryBuilder.cs`, `Src/Tests/Welfare/*` (escalation + normalization + RAG).
**Edited:** `Src/Ministers/Welfare/Rules.cs` (escalation fallthrough → `Escalate` decision), `Src/Ministers/Welfare/MinisterOfWelfare.cs` (LLM arms + deps, no bootstrap), `Src/Coordination/MinisterRegistry.cs` (LLM flags + views), `Src/ApiHost/Program.cs` (DI).

## 7. Suggested gimp slicing

- **C1 — escalate seam:** WC1 (`Escalate` fallthrough) + WC3 minister LLM arms wired to a stub/echo response + rules-escalation tests. Proves the seam without prompt/RAG quality.
- **C2 — prompt + normalize:** WC2 (`welfare.system.md`) + WC4 (parser/normalizer) + normalization tests.
- **C3 — RAG + activation:** WC5 (retriever/query builder) + WC6 (registry flip + DI) + manual-ingestion/replay tests + live verify (snapshot/replay JSON first, per memory).

Land C1→C2→C3.
