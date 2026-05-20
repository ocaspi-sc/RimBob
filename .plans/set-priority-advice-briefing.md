# `set_priority` Advice — Briefing Read (Track A) — Plan

> Agent-created plan. **Near-term, Suggest-only.** Implements the *read* half of
> the [`Docs/DESIGN.md`](../Docs/DESIGN.md) decision "Auto execution delegates to the
> game's native automation": let ministers emit accurate work-priority advice.
> No game writes. The deferred Auto *write* half is a separate plan:
> [`auto-knob-write-shim.md`](auto-knob-write-shim.md).

---

## 1. Goal

Ministers should be able to say *"raise Bob's Cook priority — he is the only
able cook and meals are backing up"* and have it be **true against current game
state**. Today they can emit the action but cannot ground it: no briefing
carries current work priorities, so a minister cannot tell whether the knob is
already set or who the single point of failure is.

Outcome: the work tab becomes legible advice vocabulary. The game still does all
allocation; RimBob only *recommends* policy and the player keeps control.

## 2. Key finding — the advice vocabulary already exists; the input does not

Verified in this repo:

- `AdviceActionKind.SetPriority` / `SetSchedule` already exist
  ([`Src/Common/Advice/AdviceAction.cs:96-97`](../Src/Common/Advice/AdviceAction.cs)).
- The normalizer already infers `work_type` for `SetPriority`
  ([`Src/LlmGateway/AdviceActionNormalizer.cs:63-69`](../Src/LlmGateway/AdviceActionNormalizer.cs)).
- `AdviceApplyKind` is `MarkHarvestArea`, `MarkHuntArea`, `UnforbidThings`,
  `UpsertProductionBill` only — `set_priority` is correctly **emit-only /
  Suggest** ([`AdviceAction.cs:69-75`](../Src/Common/Advice/AdviceAction.cs)).

So **no enum or parser work is needed**. The whole slice is an *input* gap:
ministers need a current-priority/coverage signal in their briefing.

## 3. Gating decision — does the RIMAPI fork expose a work-priority GET?

`Docs/design/RimAPI.md` lists **Pawn Edit Controller** as known-but-uncached and
the likely work-priority surface. Before any Track A code:

- [ ] Fetch upstream/fork docs; confirm a **GET** returning per-pawn work
      priorities (ideally assigned policies/schedule too). Cache the verified
      shape into `Docs/design/RimAPI.md` (same convention as other verified
      shapes).
- [ ] If only a write exists and no read: that is a **cross-repo fork endpoint
      task** (`C:\dev\RIMAPI-for-RimBob`), analogous to
      [`rimapi-blueprint-placement-endpoint.md`](rimapi-blueprint-placement-endpoint.md).
      Add the GET there first. RimBob stays HTTP-only, no linking (GPL-3.0).

This is the real gate, not the HTTP plumbing. **This is Slice 0.**

## 4. Code changes (this repo)

1. **`Src/GameStateSync/RimApiClient.cs`** — one enveloped GET method
   (`GetWorkPrioritiesAsync` or similar). Add DTOs under `Src/Ingestion/Dtos/`.
   Keep the client-header rule: add the method only when the first consuming
   minister actually needs it (Food, below).
2. **State-store derivation** — a *compact derived* view, never the raw
   pawn×worktype grid (briefings are the quality lever; "compact opportunity
   summaries over raw dumps"). Start minimal:
   - work types with no pawn above a priority threshold;
   - single-point-of-failure: a work type covered only by one pawn who is also
     the only cover for another critical type;
   - obvious skill/coverage gaps.
   Mirror
   [`FoodBriefingDerivation`](../Src/StateStore/Derivations/FoodBriefingDerivation.cs)
   style and test shape.
3. **Briefing field** — add the compact coverage signal to the first consumer's
   briefing. **Food is the natural first consumer**: it already emits
   work-type-qualified labor requests, so the signal directly sharpens existing
   advice. Keep the field tight.
4. **Minister rule / prompt** — Food emits a `set_priority` action when the
   coverage signal shows a concrete, actionable gap tied to a real food problem
   (e.g. only-cook overloaded while meals back up). Rules-first; LLM escalation
   only for genuine tradeoffs. No executable handle attached (Suggest-only).
5. **Dashboard** (per `AGENTS.md` dashboard rule) — render `set_priority`
   actions clearly (owner + work type + target priority). Add a small
   work-coverage panel on the Food tab so the advice is inspectable against
   current state. **No Apply button.**

## 5. Risks & mitigations

- **Stomping player intent.** Advice-only; the player keeps control. No write
  path exists in this plan.
- **Briefing bloat.** Mitigated by deriving a coverage *summary*; never ship
  the raw matrix into the prompt.
- **Noisy priority nagging.** Rule must require the gap to be coupled to an
  actual domain problem (meals backing up), not "a number could be higher."
- **Cross-repo drift.** If a fork GET is added it ships as a separate mod
  build; the client must degrade to a `rimapi_unavailable`-style path on
  404/501 (existing precedent in `RimApiClient`).

## 6. Testing

- `RimApiClientTests` for the new GET (envelope + 404 → unavailable).
- State-store derivation unit tests (coverage-gap math, single-point-of-failure
  detection) with fixtures.
- Food rule/fixture tests: a coverage-gap briefing yields a `set_priority`
  action with correct `work_type`/`owner`; a well-covered briefing yields none
  (no noise).
- `dotnet test`, `dotnet build Src/RimBob.sln`, `npm.cmd run build`,
  `./run-rimbob.ps1` (worktree → non-5000 port) health smoke.

## 7. Slicing

- **Slice 0 (gate, no code):** confirm/cache the Pawn Edit Controller GET shape
  in `Docs/design/RimAPI.md`; if absent, fork-GET task first.
- **Slice 1:** RimApiClient GET + DTOs + coverage derivation + Food briefing
  field + Food `set_priority` rule + dashboard rendering. Ship independently,
  Suggest-only.
- **Slice 2:** thin Welfare schedule-coverage signal reusing the same
  derivation, once Welfare ships.

## 8. Open questions

- [ ] Does the RIMAPI fork expose a work-priority/policy **GET**? (Slice 0.)
- [ ] First consumer: Food only, or also a thin Labor-in-Suggest coverage
      advisor?
- [ ] Exact derived signals — start minimal, grow from replay/pushback
      evidence.
- [ ] Welfare vs Labor ownership of schedule-coverage advice (ties to
      [`ministers/welfare.md`](../Docs/design/ministers/welfare.md) open question).
