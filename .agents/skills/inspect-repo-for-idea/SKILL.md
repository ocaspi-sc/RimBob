---
name: inspect-repo-for-idea
description: Inspect an external repository or local codebase for useful ideas, concepts, abstractions, algorithms, endpoints, UI patterns, or tooling that could improve RimBob, one of its ministers, the dashboard, RIMAPI integration, or developer workflows. Use when asked to clone, review, compare, mine, or research another project for RimBob ideas, and when asked to turn those findings into RimMind todo entries.
---

# Inspect Repo For Idea

Review another codebase as a source of design and implementation ideas for RimBob. Default to analysis and recommendations; do not edit RimBob code unless the user explicitly asks to implement. If the user asks for durable follow-ups, write todo lines only. Do not create plan files unless explicitly requested.

## Workflow

1. Ground the review in RimBob.
   - Read `Docs/DESIGN.md`, `HumanTodo.md`, and the relevant focus docs before judging fit.
   - Common focus docs: `Docs/design/ministers.md`, `Docs/design/state-store.md`, `Docs/design/advice.md`, `Docs/design/dashboard.md`, `Docs/design/RimAPI.md`, and `Docs/design/ministers/<name>.md`.
   - Preserve core constraints: suggest-first MVP, rules-first ministers, compact briefings, no direct minister-to-RIMAPI calls, player-confirmed Assisted Apply only, and Auto/HTN/Labor deferred.

2. Acquire the reference repo.
   - If given a GitHub URL, clone it under `var/research/<repo-name>` for temporary inspection unless the user asks for a persistent location such as `C:\dev\<repo-name>`.
   - If network sandboxing blocks the clone or fetch, rerun the same Git command with escalation.
   - Record the clone path, remote URL, current branch, HEAD commit, and license.
   - Never stage cloned repo contents under RimBob. `var/` is gitignored and should remain untracked.

3. Map the repository shape.
   - Start with `README`, license, solution/package files, project layout, and `rg --files`.
   - Identify runtime model: in-game mod vs external service, language/runtime, persistence, UI, tool system, planner, event loop, data snapshots, and tests.
   - Search for implementation clusters, not just names: `service`, `tool`, `planner`, `snapshot`, `briefing`, `cache`, `risk`, `score`, `cooldown`, `history`, `prompt`, `event`, `apply`, `designation`, `construction`, `research`, `medical`, `mood`, `trade`, `defense`.

4. Extract transferable ideas.
   - For each promising idea, inspect the source enough to understand the actual mechanism, not only the README claim.
   - Classify each idea:
     - `Adopt soon`: fits RimBob's current architecture and needs only state-store/briefing or small UI work.
     - `Adapt later`: useful but blocked by missing RIMAPI data, later minister scope, or Auto-only behavior.
     - `Avoid`: conflicts with RimBob's external-service, suggest-first, rules-first, or debug-first design.
   - Note required RimBob prerequisites such as RIMAPI endpoints, state-store aggregates, briefing fields, dashboard metadata, tests, or replay corpus fields.

5. Compare against RimBob architecture.
   - Prefer translating ideas into RimBob's existing seams: `IngestionDispatcher -> ColonyState -> BriefingCache`, minister rules, `AdviceItem.actions[]`, dashboard SYSTEM/debug panels, replay corpus, or developer skills.
   - Do not recommend copying whole architectures across incompatible boundaries, especially Unity/RimWorld mods, service locators, autonomous action systems, or broad write tooling.
   - If code copying is genuinely recommended, call out license obligations. Prefer concepts and algorithms over copied code.

6. Report the review.
   - Lead with clone facts and the main conclusion.
   - Then list the best ideas with local source links and RimBob target areas.
   - Include a short `Avoid` or `Not now` section when relevant.
   - End with a ranked next-order recommendation if there are multiple actionable ideas.
   - Say what was verified and what was not; todo-only or review-only work does not need builds/tests.

## RimMind Todo Capture

When the user asks to capture findings in `HumanTodo.md`:

1. Add lines under `## Captured by /todo`, immediately after `<!-- entries go here -->`.
2. Use this format:
   ```md
   - [ ] [YYYY-MM-DD] #rimmind #tag RimMind: IDEA_TITLE - short imperative investigation. [Source](C:/path/to/file.cs)
   ```
3. Use `RimMind: IDEA_TITLE` exactly for the todo title prefix.
4. Include tags that make the future slice easy to find, such as `#food`, `#construction`, `#defense`, `#research`, `#welfare`, `#medical`, `#storage`, `#tooling`, `#cos`, `#dashboard`, `#rimapi`, `#replay`, or `#prompts`.
5. Link to source files in the persistent clone when one exists. Use angle-bracket links for paths with spaces.
6. Split broad ideas into multiple todos when they target different ministers, endpoints, or implementation seams.
7. Keep each line short enough to scan. Do not write paragraphs into `HumanTodo.md`.
8. Verify every new local file link exists before committing.
9. Do not generate `Docs/plans/` artifacts unless the user explicitly asks for a plan.

## Closeout

If the user asks to land, commit, or close the worktree, use `close-out-session` after the review/todo edits are complete. Stage only session-owned files, preserve unrelated dirty work, land on the real `C:\dev\RimBob` `master`, and report any Windows worktree folder that Git deregisters but cannot physically delete.
