# RimBob - Agent Instructions

This file is loaded by Codex at the start of every session. Keep it as an operating manual for agents. Product design truth lives in `Docs/DESIGN.md` and the linked design docs.

---

## Agent Discipline

- Read `SOUL.md` for interaction defaults, then read `Docs/DESIGN.md`, `Tasks.md`, and the relevant focus doc before changing files.
- Store agent-created plans in `.plans/`. At session end, offer to update `Tasks.md` with new tasks uncovered.
- This project is maintained simultaneously by multiple AI agents from different companies.
- After finishing a change, make sure I can see it. Rebuild if necessary, and include a clickable URL or file link to the result.
- Use cheap subagents often
- Show me your plan before doing any significant work
- When writing markdown (.md) files, don't add your own linebreaks in the middle of the paragraph. the viewer has line wrapping.
- If unsure whether the user wants planning-only or land-it, ask. Do not infer "ship" from a plan request.
- While reading the code, if you see something confusing, ill-designed, convoluted, over-engineered, tech-debt: Suggest refactoring ideas!

## Coding

- Write SOLID code
- We don't care about legacy or breaking changes or compatibility. Be brave.
  - **No legacy/compat code paths.** Concretely forbidden:
    - tolerant parsers whose only job is to swallow a removed shape
    - `if (legacy_format) ...` branches
    - upgrade-migration code for persisted state — on schema/wire change, **wipe persisted state and regenerate**, do not maintain a read path for the old shape
    - "soft-fail and null it" fallbacks targeted at a specific known-removed field
  - **Robust error handling is fine; compat is not.** Robust = catches malformed input in general. Compat = handles a *specific known-removed* format. The first is engineering; the second is forbidden — do not let the first label disguise the second.
  - When a plan changes a wire or persistence format, the plan must state explicitly: *"no compat code; wipe-and-regen on upgrade."* Verifier flags any such code as out-of-scope.
- Be generous with adding //todo comments
- Write self documenting code. Descriptive Names are very important.
- Write short WHY comments to provide context for future readers.

---

## GIT

### Repository Shape

- `var/` is gitignored (icons, embeddings, agenda, replay corpus). Never stage anything under `var/`.
- `C:\dev\RimBob` should always stay on master branch.
- The only unstaged changes on `C:\dev\RimBob` should be manual edits by the human.
- Master is the only integration point. Never merge or cherry-pick another agent's unlanded feature branch.

### Worktree Flow

- When changing code, make sure it's in a worktree + feature branch that's correct for the current task. If not, create a worktree first based off current master and work there, using commits generously.
- When finished, the usual MO is to squash-merge the feature branch into master so it lands, then remove the worktree.
- Sync before verifying: before any build that validates behavior or gates a land, run `git merge master` in the worktree so you build the integrated result, not a stale snapshot missing changes other agents already landed.
- Resolve conflicts before building; never skip the sync to dodge them. If `master` is being written by another session, apply the git wait-and-retry rule below.
- Shrink the staleness window; do not sync across worktrees. Keep slices small and squash-merge to master as soon as a slice is green, so other worktrees are never far behind.
- If a task grows large, split it and land the independent parts early rather than letting one branch diverge.

### Main Checkout Safety

- On entry to a session on `C:\dev\RimBob`, run `git status --short` before any staging. If the index is not clean and you did not stage it yourself, do not run `git add` or `git commit`; report the foreign staged paths and ask for human adjudication.
- Use an advisory main-checkout write lock for index-mutating work on `master`: `C:\dev\RimBob\.git\rimbob-master.lock`, containing one JSON line with `pid`, `agent`, `started_at`, and `intent`.
- Acquire the lock with `New-Item` only when the file is absent. Wait or report if it exists. Treat locks older than 10 minutes as stale only with a logged takeover.
- Release the lock after the commit succeeds and `git status --short` is clean.
- Multi-step git operations on the main checkout are forbidden. If a task needs more than one `git mv`, `git rm`, or staged edits across multiple files that are not all going into one immediate commit, do it in a worktree and land through `master` after the slice is green.

### Staging And Commits

- On `C:\dev\RimBob`, index-mutating commands (`git mv`, `git rm`, `git add`, and similar) must be followed in the same tool invocation by a staged-manifest check and a `git commit` that lands exactly the intended files. If you cannot commit immediately, do not stage.
- Before every commit on `master`, run `git diff --cached --name-only` and confirm the output exactly matches the intended file set for that commit. If unexpected paths appear, do not commit a superset; unstage only paths you just staged or stop and report.
- Banned on `C:\dev\RimBob`: `git add -A`, `git add .`, `git add --all`, `git add -u`, `git commit -a`, and `git commit -am ...`.
- Stage only by explicit path: `git add -- <path> [<path> ...]`.
- Worktrees may relax this only when the working tree is known to contain only session-owned files.
- Commit messages should contain some tags, a title, and a summary of the changes. Write 1-5 lines depending on the size of the scope.

### Contention And Special Cases

- When doing git operations, if there's a lock file or another session appears to be writing or committing, wait briefly and retry the narrow operation; do not force broad Git actions.
- If you encounter these dirty files in `C:\dev\RimBob` on master branch with a couple of unrelated small human edits, stack them in a small commit just for those files: `AGENTS.md`, `CLAUDE.md`, `Tasks.md`, .plans/*

---

## Documentation Discipline

- Any design change updates docs in the same turn. Trigger words reinforce this rule; they are not required.
- Update the most specific doc. If the decision affects multiple areas, add a cross-reference in `Docs/DESIGN.md`'s decision log.
- When creating a new design doc, add it to the routing table below.
- Design docs capture durable decisions, ownership boundaries, runtime contracts, and open questions. Source/tests own exact signatures, DTOs, endpoints, fixtures, enums, helpers, and current rule lists.

### Design Doc Routing

| Topic | Doc to update |
|---|---|
| High-level principles, design goals | `Docs/DESIGN.md` |
| Build order, milestones | `Docs/ROADMAP.md` |
| Actionable tasks | `Tasks.md` |
| Minister shape, rules system, rule refinement | `Docs/design/ministers.md` |
| Advice schema, feedback lifecycle, autonomy dial | `Docs/design/advice.md` |
| Dashboard UI, HTTP+SSE contract, auth posture | `Docs/design/dashboard.md` |
| State store, briefings, aggregates | `Docs/design/state-store.md` |
| Mayor's Agenda: living plan, cabinet_direction, dashboard, API | `Docs/design/agenda.md` |
| Flags, inter-minister communication | `Docs/design/communication.md` |
| RAG, knowledge base | `Docs/design/rag.md` |
| Evaluation, improvement loop | `Docs/design/evaluation.md` |
| Code structure, stack, interfaces | `Docs/design/architecture.md` |
| RIMAPI endpoints, conventions, controller catalogue | [`Docs/design/RimAPI.md`](Docs/design/RimAPI.md) |
| Food minister, food chain, harvesting/cooking/storage | `Docs/design/ministers/food.md` |
| Specific minister | `Docs/design/ministers/<name>.md` |
| HTN, planning (deferred - Auto epic) | `Docs/design/planning.md` |
| Labor / assignment solver (deferred - Auto epic) | `Docs/design/ministers/labor.md` |

---

## Project Invariants

- RIMAPI-for-RimBob is our fork of the RIMAPI mod at `C:\dev\RIMAPI-for-RimBob` (repo: `ocaspi-sc/RIMAPI-for-RimBob`), not the upstream `IlyaChichkov/RIMAPI`. RimBob still integrates over HTTP at `http://localhost:8765/`; nothing in this repo links against the mod. RIMAPI-for-RimBob targets RimWorld 1.6 only.
- RimBob is an assisted-gameplay advisor for RimWorld. The player keeps control.
- MVP posture is **suggest + player-confirmed apply** (not suggest-only). Ministers emit advice; the player applies a growing-but-partial allowlist of non-pawn RIMAPI writes by clicking Apply. Every write is player-click-gated — this is NOT `Auto`/autonomous, and coverage stays partial (far from full control). Per-minister `Auto` (no per-click execution) is deferred until M7+ and requires explicit player consent.
- Mayor publishes the Agenda. Feeder ministers publish `AdviceItem`s. No minister autonomously writes to RIMAPI; the Host executes only allowlisted single-operation writes, and only on an explicit player Apply click.
- No direct minister-to-minister communication. Coordination flows through flags; CoS arbitrates; Mayor synthesizes.
- Rules handle common cases first. LLMs run only on escalation.
- Briefings are the quality lever: keep them tight; derived facts belong in the state store, not prompts.
- Dashboard and Host are localhost-only. Host binds `127.0.0.1`, never `0.0.0.0`.
- Deferred Auto epic: HTN planner, bulletin board, Labor solver, broad RIMAPI write coverage, and "only Labor touches pawn allocation." Do not implement before M7. (The narrow allowlisted Assisted Apply writes are the only MVP exception.)
- Dashboard is for debugging. It should reflect exact state.
- When adding new features, note how it should be reflected in the dashboard. maybe suggest a new panel.

---

## Build And Verification

- Sync the worktree with `master` before verification builds (see GIT → "Sync before verifying"). A build missing already-landed changes is not a valid verification.
- After build verification, run RimBob again and verify the Host is reachable, especially if a live `RimBob.Host` process was stopped.
- Prefer `.\run-rimbob.ps1` after builds. The launcher starts the already-built Host by default; use positive build/setup flags only when needed (`-BuildHost`, `-BuildDashboard`, `-InstallDashboard`, `-Restore`). Use `.\run-rimbob.ps1 -Foreground` when terminal output must stay attached.
- Port `5000` is reserved for the main `C:\dev\RimBob` checkout. When running RimBob from any worktree, use a different `-ListenUrl` / port, then verify `/api/system/health` and the `RimBob.Host.exe` process path before calling the worktree build live.
- After landing a change in a worktree, launch RimBob from that worktree on its own non-`5000` port and give me the worktree's dashboard URL, so I can verify the change visually against that worktree's build before it merges to master.
- Use manual `npm.cmd run build` / `dotnet run` only when debugging one side of the stack.
- After rebuilding or replacing the RIMAPI fork DLL, restart RimWorld before live verification. RimWorld does not hot-reload mod assemblies; verify `/api/v1/dev/endpoints` after restart before diagnosing RimBob.
- When adding backend logs, replay corpus files, prompt dumps, traces, or diagnostics, update dashboard-visible metadata in the same turn. If intentionally hidden, add a concrete `Tasks.md` follow-up and mention it in the final response.

## Minister Output / Log Inspection

- When asked to show a minister's current output without rendering the dashboard, do not start with broad repo searches. First call `/api/system/health` if the Host is reachable and read `storage`, `minister_outputs`, and `logs` paths from that response.
- Use the narrow live surfaces first: `GET /api/ministers/{minister}/snapshot` for typed output and `GET /api/ministers/{minister}/trace/latest` for the latest trigger/rules/LLM path.
- If the Host is down or the endpoint is unavailable, read the persisted typed snapshot at `%LOCALAPPDATA%\RimBob\ministers\<minister>.json`, then the replay/audit record at `%LOCALAPPDATA%\RimBob\logs\replay\<minister>-YYYYMMDD.jsonl`, then the human/debug logs at `%LOCALAPPDATA%\RimBob\logs\rimbob-*.log`.
- Report the advice title, body, action instructions, rule trace, flags, state summary, generation or persisted timestamp, and whether any `apply` payload or `options[]` exist. If the Host was reachable and then goes down, say so and continue from the persisted files.

---

## Repo Conventions

- All projects use `.NET 9` and latest C#.
- Use async for I/O.
- Avoid `var`; spell out types.
- No code in pure domain projects should take external dependencies.
- Every new minister gets its own directory under `Src/Ministers/<Name>/`.
- `Rules.cs` is the first file in every minister directory. It must compile and have tests before the LLM is wired.
- Fixture JSON files live in `Src/Tests/<MinisterName>/Fixtures/`.
- Aspirational "reach-target" tests are tagged `[Trait("kind", "reach")]`: they assert where advice *should* land and are expected RED until the rules catch up. The default/CI gate runs `dotnet test --filter "kind!=reach"`; run the targets on demand with `--filter "kind=reach"`. Keep real regression tests plain `[Fact]` so an already-red reach set can never mask them. (Current reach set + per-target rationale: `.plans/advice-for-new-colony-1.md`.)
- Use `// TODO:` comments only for known gaps, unverified field names, deferred writes, or concrete revisit points before the next slice ships.
- React collapsible UI uses the standard disclosure pattern: a real `<button>` header with `aria-expanded` / `aria-controls`, plus a conditionally rendered panel in normal flow.
