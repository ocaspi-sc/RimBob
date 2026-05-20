---
name: bring-out-the-gimp
description: Execute a planned slice through Codex CLI while Claude stays the principal. Use when the user wants Claude to actually land a change ("do it", "build this", "ship it", "land the plan", "run this through Codex") instead of just writing the plan. Claude writes the plan, delegates implementation to Codex in an isolated branch/worktree, verifies plan adherence with a cheap Sonnet sub-agent, iterates via the same resumable Codex session, and has Codex land to master at the end.
---

# bring-out-the-gimp

Claude-only skill. Claude is the principal: it plans, delegates, verifies, summarizes, and decides when to land. Codex is the gimp: it does the actual coding inside its own branch and worktree, keeping one long-lived session so context is preserved across iterations.

Use this whenever the user wants real code shipped. The default Claude-on-master rule ("Claude only writes plans") is satisfied because Claude still does not touch source on master — Codex does, inside a worktree, and lands a squash on master at the end.

## Roles

- **Claude (this session)** — writes the plan to `.plans/<slug>.md` (uncommitted on master), spawns Codex, drives iterations, runs the Sonnet verifier sub-agent, writes the human-facing summary back into the plan file, and tells Codex when to land.
- **Codex (via `codex.cmd exec` + resume)** — owns the feature branch + worktree, implements the plan, runs the verification commands the plan lists, self-reviews, fixes its own review items, iterates against verifier feedback, then runs the helper's `CloseOut -LandAndClose -Verified` to squash into master and remove the worktree.
- **Sonnet 4.6 sub-agent (Claude's Agent tool, `model: "sonnet"`)** — reads the plan and the worktree diff, reports plan adherence only (extras, gaps, scope drift). Not a build runner — Codex runs the build/tests per the plan.

## Default posture

- The plan file lives at `C:\dev\RimBob\.plans\<slug>.md` and stays **uncommitted** on master during the run. The absolute path is passed into Codex's prompt. Claude appends the human-facing summary to the same file at the end.
- Strongest Codex model by default: `gpt-5.5` with `model_reasoning_effort="xhigh"`. Do not switch to fast mode unless the user explicitly asks.
- Use `codex.cmd`, not `codex` or `codex.ps1`.
- Base RimBob runs from `C:\dev\RimBob`. Codex never works in `C:\dev\RimBob` itself — it gets a worktree under `%USERPROFILE%\.codex\worktrees\prompt-runs\<run-id>\RimBob`.
- One run = one Codex session. Resume the same session for every iteration and for the final land. Do not start a fresh `Start` for fixups; that loses context.
- Resumability beats tidiness. Do not delete the worktree until the slice is durably landed on master and the verifier is green.
- Codex may make branch-local commits freely on its own branch. It must not touch `master`, other branches, or other worktrees until the explicit land step.

## Helper script

Reused as-is from the prior `run-prompt-with-codex` skill; filename unchanged:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1
```

Modes: `Start`, `Resume`, `Show`, `CloseOut`. Records live under `%USERPROFILE%\.codex\prompt-runs\<run-id>\` (prompt, JSONL events, final-message files, `metadata.json` with branch/worktree/session_id).

## Full flow

### 1. Write the plan

Claude writes `C:\dev\RimBob\.plans\<slug>.md`. Required sections:

- **Motivation** — why we're doing this. Include the user's framing.
- **Context** — relevant files, design docs, prior incidents, constraints.
- **Scope** — bullet list of exactly what changes; explicit non-goals.
- **Approach** — step-by-step implementation Codex should follow.
- **Verification** — concrete commands Codex must run (e.g. `dotnet build`, `npm.cmd run build`, targeted tests, `/api/system/health` smoke) and pass/fail criteria.
- **Where to see it (dashboard)** — which panel/endpoint a human checks to confirm the change is live. If no dashboard surface exists, say so and add a follow-up.
- **Open questions** — anything Codex should escalate rather than guess at.

Link the plan from `HumanTodo.md` per the normal rule. Do not commit it yet — it stays as a dirty file on master while the run is open.

### 2. Pre-flight

```powershell
git -C C:\dev\RimBob status --short --branch --untracked-files=all
git -C C:\dev\RimBob worktree list --porcelain
```

If master is dirty with foreign changes, stop and report — do not start a Codex run on top of someone else's in-flight work.

### 3. Start the Codex run

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Start `
  -Name "<slug>" `
  -Prompt @'
You are the implementation gimp for a Claude-driven slice. Read these first, in order:
  1. C:\dev\RimBob\AGENTS.md
  2. C:\dev\RimBob\.plans\<slug>.md   <-- the plan; this is your source of truth
  3. Any focus docs the plan references.

Operating constraints:
  - You are in your own worktree on branch codex/prompt-<run-id>. Do not touch C:\dev\RimBob master, other branches, or other worktrees.
  - Implement the plan's Scope. Do not exceed it. If you discover the plan is wrong, stop and leave a final message explaining why instead of inventing scope.
  - Make branch-local commits for coherent slices. Commit messages must use the repo's tag+title+summary style.
  - Run every command in the plan's Verification section. Fix what fails. Do not declare done while anything in Verification is red.
  - Self-review your own diff before declaring done: scan for accidental edits, stray var/ files, dead code, missing tests, and inconsistencies vs the plan. Fix what you find.
  - Final message must list: changed files, commits made (hashes + subjects), verification commands run and their result, anything skipped or deferred, and any blocker.
  - Do not run the closeout/land step yet. Claude will tell you when.
'@
```

Capture the printed `run_id`, `session_id`, `branch`, `worktree`. Report them to the user so the run is resumable from another session if needed.

### 4. Inspect what Codex did

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Show `
  -RunId "<run-id>"
```

Read the latest `final-message-*.md` and `git -C <worktree> diff --stat`. If Codex bailed (non-zero exit, blocker reported, plan declared wrong), surface that to the user — do not paper over it with a verifier run.

### 5. Verify plan adherence (Sonnet 4.6 sub-agent)

Spawn a single Agent with `model: "sonnet"` (cheap), `subagent_type: "general-purpose"`. Brief it like a fresh reviewer:

- The absolute path to `.plans/<slug>.md`.
- The worktree path.
- The exact question: "Does the diff at `<worktree>` implement the Scope in `<plan>` — nothing more, nothing less? List adherence gaps and out-of-scope changes only. Do not opine on style, do not run builds, do not check tests — Codex already ran the plan's verification."
- Ask for a short structured report: `Adherent: yes/no`, `Gaps:` (bulleted), `Out-of-scope changes:` (bulleted), `Notes:` (one paragraph max).

The sub-agent is intentionally narrow. Anything outside plan-vs-diff is Codex's job, not the verifier's.

### 6. Iterate (Resume the same Codex session)

If the verifier reports gaps or out-of-scope changes:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Resume `
  -RunId "<run-id>" `
  -Prompt @'
Verifier reported these plan-adherence items. Fix them, then re-run the plan's Verification commands. Do not expand scope to address related-but-unlisted issues; if you think the plan itself is wrong, stop and say so.

Gaps:
  - ...

Out-of-scope changes to revert:
  - ...
'@
```

Loop steps 4-6 until the verifier returns `Adherent: yes`. Codex's context is preserved across resumes — it remembers what it built and why, so iteration is cheap.

If a loop stalls (same gap twice, Codex pushing back on the plan, repeated build failures), stop iterating and surface to the user. Plan probably needs human revision.

### 7. Claude writes the human-facing summary

Append a new section to `.plans/<slug>.md` (still uncommitted on master):

```markdown
---

## Summary (landed <date>)

**Motivation.** <one paragraph, in the user's framing>

**Context.** <links to docs, prior decisions, related slices>

**Scope.** <bullets of what actually shipped>

**How to verify (human).**
  - Dashboard: <URL path, panel name>
  - Commands: <copy-pasteable smoke checks>
  - Files to glance at: <key paths>

**Codex run:** <run-id> · branch `codex/prompt-<run-id>` · landed commit `<filled at step 8>`
```

This is the durable handoff. The plan file ends up as both the intent and the changelog for this slice.

### 8. Land

Resume Codex one more time with the land prompt:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Resume `
  -RunId "<run-id>" `
  -Prompt @'
Verifier is green. Land this slice on master.

Pre-flight: confirm your worktree is clean, your branch is ahead of master and not behind. If it is behind, `git merge master` first, resolve conflicts, re-run the plan's Verification, and only proceed when green.

Then invoke the helper to land:
  powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1 `
    -Mode CloseOut -RunId <run-id> -LandAndClose -Verified

The helper enforces: clean worktree, branch ahead of master and not behind, C:\dev\RimBob on master, C:\dev\RimBob clean, branch has commits to land. If any guard trips, stop and report — do not work around the guards.

Final message: landed commit hash, branch + worktree removal confirmation, any post-land caveats.
'@
```

After it returns, read the new landed commit hash and fill it into the plan's Summary section. Then commit the plan + summary on master with an explicit path stage (per AGENTS.md):

```powershell
git -C C:\dev\RimBob add -- .plans/<slug>.md
git -C C:\dev\RimBob diff --cached --name-only   # confirm only the plan
git -C C:\dev\RimBob commit -m "[plans] <slug>: record summary and landed commit"
```

### 9. Report

Reply to the user with: landed commit hash, dashboard URL/panel from the Summary, plan path, run id (in case they want to inspect events later).

## Failure cases

- **Codex declares the plan wrong.** Do not resume with "ignore your concern". Surface the pushback to the user. Edit the plan, re-Start a fresh run (new run id, new session) if the change is substantive.
- **Verifier stuck reporting the same gap twice.** Codex either can't or won't fix it. Surface to the user; consider plan revision or human intervention.
- **CloseOut guard fails.** Almost always means master moved while the run was open. Resume Codex with "merge master, re-verify, then re-attempt CloseOut". Never bypass with `--no-verify` or by editing master out of band.
- **Worktree deleted mid-run.** Repository-local continuation is dead. The CLI session may still be resumable, but the diff and files are gone. Tell the user before doing anything else.

## What this skill is not

- Not for design questions, "what if" explorations, or refactor brainstorms. Those stay in Claude's own session.
- Not for one-line typo fixes, doc tweaks, or HumanTodo edits. Those don't need a worktree.
- Not a way to bypass the verifier. The Sonnet sub-agent step is required; "looks fine to me" is not a substitute.
