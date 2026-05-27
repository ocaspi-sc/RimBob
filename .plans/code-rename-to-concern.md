# Code rename: `advice_type` → `concern`

## Motivation

The design-language concept "advice type" was renamed to "concern" this design session. Rationale: "advice type" is a generic CS word; the concept is really the **autonomy-dial graduation unit** (per `Docs/design/advice.md`). "Concern" reads naturally in both Suggest mode (minister surfaces a concern) and Auto (player trusts minister with a concern).

The design docs are being updated by a **parallel sonnet doc-rename slice** (background subagent at the time of writing this plan). This slice lands the code-side rename so doc/code drift is short-lived. Pure rename, no semantic change.

## Context

- `Docs/design/advice.md` — central design doc; "Advice Type" section is being renamed to "Concern" by the parallel doc slice.
- `Src/Common/Advice/AdviceItem.cs` — carries the property (`AdviceType` or similar) and `[JsonPropertyName("advice_type")]`.
- `Src/Common/Advice/FoodAdviceType.cs` — the only existing per-minister enum (Food is the only fully-built feeder; Mayor uses `AgendaPriority`, not a concern enum).
- `Src/LlmGateway/prompts/*.system.md` — LLM prompts reference the `advice_type` JSON field by name.
- `Src/Tests/**/Fixtures/*.json` — test fixtures carry `"advice_type": ...`.
- `Dashboard/src/**/*.{ts,tsx}` — TypeScript mirrors of `AdviceItem` + any UI label that says "Advice Type".
- Replay corpus JSONL (`replay/food-YYYYMMDD.jsonl` per the `replay-records` decision) — historical records carry `"advice_type"`. Per `AGENTS.md` ("we don't care about legacy or breaking changes — be brave"), we break the historical key cleanly **but** keep a tolerant inbound reader because the refinement loop wants to replay those records.
- Sonnet doc subagent's scope explicitly **kept** code-symbol references (e.g. `FoodAdviceType`) in design docs — they get cleaned up in a follow-up "doc/code symbol sync" pass once this lands.

## Scope

> **No compat code; wipe-and-regen on upgrade.** This is a wire + persistence rename. Per `AGENTS.md` "No legacy/compat code paths" rule: NO tolerant parsers that swallow removed shapes, NO legacy-keyed read paths, NO migration shims. On schema/wire change, persisted state (replay corpus JSONL) is **operationally wiped and regenerated** — not code-supported. Verifier flags any such compat code as out-of-scope.

**In scope (Codex MUST do all of this):**

1. **C# property rename:** in `Src/Common/Advice/AdviceItem.cs`, the per-item category property (currently named `AdviceType` or similar — verify) → `Concern`. Update the `[JsonPropertyName(...)]` attribute from `"advice_type"` → `"concern"`. Update any `[JsonConstructor]` / record-positional parameter name.
2. **C# enum rename + file rename:** `Src/Common/Advice/FoodAdviceType.cs` → `Src/Common/Advice/FoodConcern.cs` (use `git mv` from inside the worktree). Inside, `public enum FoodAdviceType` → `public enum FoodConcern`. The enum members keep their existing names (`FoodSecurity`, `ExpandGrowingCapacity`, etc.) — only the enum type renames.
3. **Sweep all C# references** in `Src/**/*.cs` (incl. tests): every `FoodAdviceType` → `FoodConcern`; every property access `.AdviceType` that refers to the renamed `AdviceItem.Concern` → `.Concern`. Be careful not to rename any `AdviceType` that is **not** the AdviceItem concept (none expected, but verify with grep).
4. **JSON wire field everywhere:** every `"advice_type"` literal in C# source (e.g. `[JsonPropertyName]`, dictionary keys, JSON-string literals) → `"concern"`.
5. **LLM prompts:** every `.system.md` (and any `.user.md`/template files) under `Src/LlmGateway/prompts/` and any other prompt root — replace `advice_type` field references with `concern`; update inline prose that names the schema field (e.g. "emit `advice_type` per the enum below" → "emit `concern` ..."). Do **not** rewrite prompt logic, only the field name + the inline prose around it.
6. **Test fixtures:** every `Src/Tests/**/Fixtures/*.json` — `"advice_type"` key → `"concern"`. **No** legacy-keyed fixture survives (there is no tolerant path to test, per #8). Delete any fixture whose only purpose was legacy-key testing.
7. **Dashboard TypeScript:** sweep `Dashboard/src/**/*.{ts,tsx}` — mirror type fields `advice_type` → `concern`; type literals/unions like `'FoodAdviceType'` → `'FoodConcern'` if any; UI labels rendering the string "Advice Type" / "Advice type" → "Concern" / "Concern". Update Storybook fixtures if any.
8. **No tolerant inbound parser.** Per `AGENTS.md` no-legacy rule, the LLM-response parser reads **only** `concern`. A payload arriving with `advice_type` fails normal parsing — that is correct, not a regression. Outbound emit is always `concern`. Do NOT add an `advice_type`-tolerant accept-both branch or any related `// TODO:` comment.
9. **Replay corpus: wipe-and-regen.** Do NOT add a legacy-keyed reader for historical `replay/*.jsonl` records. The replay reader requires `concern` only. Historical Food records carrying `advice_type` are operationally wiped (operator deletes the `replay/` dir on upgrade); new records start fresh in `concern`-keyed form. No code path supports the old key.

**Non-goals (Codex MUST NOT do):**

- No `.md` design-doc edits — those are owned by the parallel sonnet doc-rename slice. Touching `Docs/**.md` or `.plans/**.md` will create merge contention.
- No `HumanTodo.md` edits — same reason; doc slice handles `advice-type-enums` → `concern-enums`.
- No semantic changes: no new advice fields, no new advice types/concerns, no behavioral changes.
- No unrelated refactors: do NOT touch the `AdviceActionApply` god-record split, advice `options[]`, `blueprint_group`, typed request arrays, or icon/reason drop — those are the `schema-landing` slice, separate.
- No replay-corpus rewriting/migration script. Just the tolerant reader.
- No version bumps, no NuGet/npm dependency changes.

## Approach

1. **Inventory pass (read-only):**
   - `Grep -rn "AdviceType" Src/ Dashboard/` (excluding `bin/`, `obj/`, `node_modules/`).
   - `Grep -rn "advice_type" Src/ Dashboard/`.
   - Report counts back in the final message so the verifier can sanity-check coverage.
2. **Rename the file** via `git mv` from within the worktree: `Src/Common/Advice/FoodAdviceType.cs` → `Src/Common/Advice/FoodConcern.cs`.
3. **Edit `AdviceItem.cs`** (property + `JsonPropertyName`).
4. **Edit `FoodConcern.cs`** (enum type name only; members stay).
5. **Project-wide sweep** for `FoodAdviceType` → `FoodConcern` and `.AdviceType` → `.Concern` (verify each `.AdviceType` is the right concept before replacing — no false positives like a type member named `AdviceType` on an unrelated record).
6. **Wire-field sweep:** `"advice_type"` → `"concern"` in all `.cs`, `.system.md`, prompt files, fixtures, TS mirrors.
7. **Tolerant parser** edit + replay reader edit + the two `// TODO:` comments.
8. **Self-review the diff** before declaring done: no stray edits in `bin/obj/var/`, no design-doc edits, no semantic changes.
9. **Run Verification** below; fix what fails. Loop until green.
10. Commit branch-locally per the repo's tag+title+summary style (e.g. `[advice] rename advice_type→concern code-wide`). One commit per coherent slice is fine; final squash happens at land.

## Verification

Codex must run **all** of these and pass. Order matters (build before test before dashboard).

```powershell
# C# build — clean, no warnings about missing AdviceType
dotnet build Src/RimBob.sln

# Tests — all green; check that fixture renames + tolerant parser are exercised
dotnet test Src/Tests/RimBob.Tests.csproj

# Dashboard build — TS compiles, no missing fields
npm.cmd run build
# (run from C:\<worktree>\Dashboard\, not the repo root inside the worktree)
```

**Residual-name checks** (grep — must be ZERO, no exceptions):

```powershell
# Run from the worktree root. No tolerant parser, no legacy fixture → zero residual.
rg -n 'AdviceType' --type cs Src/
rg -n '"advice_type"' Src/ Dashboard/
rg -n 'AdviceType' Dashboard/src/
```

Codex reports the counts (all zero) in the final message.

**Optional smoke** (skip if `RimBob.Host` isn't reachable from the worktree port):

- `Start-Process` `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5050` (or any free port — not `5000`, that's master's).
- `curl http://127.0.0.1:5050/api/system/health` → 200.
- Trigger a Food cycle (`POST /api/ministers/food/trigger`), then `GET /api/ministers/food/snapshot` → AdviceItems carry `"concern": "..."` (NOT `"advice_type"`).

## Where to see it (dashboard)

After landing + relaunching Host on `5000`:

- **Food tab** in the dashboard → any active advice card renders normally; if the inspector previously showed "Advice Type: food_security", it now shows "Concern: food_security".
- **SYSTEM tab → Raw LLM Output** (if a Food cycle ran post-deploy) → captured JSON uses `concern`.
- **API**: `GET /api/ministers/food/snapshot` → `{ "advice": [{ ..., "concern": "food_security", ... }] }`.

If no human-facing label was ever rendered for the concept, that's fine — the field rename is observable in the SYSTEM raw JSON.

## Open questions

- [x] **Replay-corpus migration:** RESOLVED — per `AGENTS.md` "No legacy/compat code paths", no tolerant reader. Historical `advice_type`-keyed records are operationally wiped on upgrade; new records start in `concern`-keyed form.
- [ ] **Doc/code symbol drift:** docs (sonnet slice) intentionally kept `FoodAdviceType` / `AdviceItem.AdviceType` as code references. After this lands, those doc mentions are stale. Follow-up todo: `doc-code-symbol-sync` (separate, cheap pass). NOT in this slice's scope.
- [x] **In-flight LLM output during the rename window:** RESOLVED — no tolerant parser per #8. The system prompts in `Src/LlmGateway/prompts/*.system.md` are updated in this slice's scope (#5), so a stale LLM emitting `advice_type` is a one-deploy-cycle non-issue (and would surface clearly as a parse error if it happened).

---

## Summary (landed 2026-05-23)

**Motivation.** The design-language concept "advice type" was renamed to **"concern"** this session. Rationale: "advice type" is a generic CS word; the concept is the autonomy-dial graduation unit + dedup key + category label (per `Docs/design/advice.md`). "Concern" reads naturally in both Suggest mode (minister surfaces a concern) and Auto (player trusts minister with a concern). This slice lands the code-side rename; a parallel sonnet doc-rename slice updates the design docs.

**Context.** Decision recorded in `Docs/DESIGN.md` decision log this session. Related: the 9 Willie advice-type values were also renamed this session (design-only, separate edit pass). The "advice action" surface (`AdviceActionKind`, `place_blueprint`, etc.) is **untouched** — distinct from concern.

**Scope shipped.**
- `AdviceItem.AdviceType` → `AdviceItem.Concern`; `[JsonPropertyName("advice_type")]` → `("concern")`.
- `FoodAdviceType` enum → `FoodConcern` (file renamed via `git mv`).
- Project-wide sweep across `Src/` and `Dashboard/`: every C# reference, JSON literal, fixture key, prompt field, and TS mirror. Final residual count: zero (`AdviceType` and `"advice_type"` both 0 in code).
- LLM prompt (`food.system.md`) updated to emit `concern` (and `allowed_concerns`).
- Dashboard UI label switched to "Concern".
- **No compat layer** per the new `AGENTS.md` "No legacy/compat code paths" rule: no tolerant parser, no legacy-keyed replay reader. Old `replay/*.jsonl` records carrying `advice_type` are operationally wiped on upgrade (delete the `replay/` dir; new records start `concern`-keyed).
- Legacy fixture `food-rules-history-legacy-advice-type.jsonl` deleted; replaced with `concern`-keyed `food-rules-history.jsonl`. Tests/helpers exercising the now-removed tolerant path deleted.

**How to verify (human).**
- **Dashboard:** Food tab → any advice card; inspector now shows "Concern: <food concern>". SYSTEM → Raw LLM Output (after a Food cycle) → captured JSON uses `"concern"`.
- **API smoke:** `curl http://localhost:5000/api/ministers/food/snapshot` → AdviceItems contain `"concern": "..."` (not `"advice_type"`).
- **Operational note:** on first run after this lands, delete the historical replay dir (`%LOCALAPPDATA%\RimBob\logs\replay\` or wherever `RimBob:LogsRoot` resolves) — old records are not readable.
- **Files to glance at:** `Src/Common/Advice/FoodConcern.cs`, `Src/Common/Advice/AdviceItem.cs`, `Src/LlmGateway/AdviceResponseNormalizer.cs`, `Src/LlmGateway/prompts/food.system.md`, `Dashboard/src/types/advice.ts`.

**Open follow-ups (separate slices).**
- Parallel sonnet doc-rename slice — `Docs/`, `.plans/`, `HumanTodo.md`, `AGENTS.md`: prose + wire token; kept PascalCase code symbols (stale after this code rename — see next bullet).
- `doc-code-symbol-sync` — once doc rename + this code rename both land, sweep design docs for stale `FoodAdviceType` / `AdviceItem.AdviceType` mentions and update to `FoodConcern` / `AdviceItem.Concern`.

**Codex run:** `20260524-214649-code-rename-to-concern` · branch `codex/prompt-20260524-214649-code-rename-to-concern` · landed commit `e882bada55dfe85c687944142cbd52b4303089ce`
