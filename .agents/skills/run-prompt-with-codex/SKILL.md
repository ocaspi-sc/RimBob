---
name: run-prompt-with-codex
description: Run or resume a Codex CLI prompt in an isolated RimBob branch/worktree. Use when the user asks to delegate a prompt to codex.cmd, keep a Codex CLI run resumable, inspect a prior delegated run, or optionally commit/land/close the delegated worktree.
---

# Run Prompt With Codex

Run a prompt through the Windows Codex CLI while keeping the work isolated, resumable, and safe to land later.

Default posture: create a feature branch + worktree, run `codex.cmd exec`, record the session id and artifacts, then leave the worktree open. Closeout is explicit.

## Key rules

- Use `codex.cmd`, not `codex` or `codex.ps1`, on this Windows machine.
- Use the strongest available Codex model by default. Current default is `gpt-5.5` with `model_reasoning_effort="xhigh"`; do not use fast mode for delegated runs unless the user explicitly asks.
- Base RimBob runs from the real checkout at `C:\dev\RimBob` unless the user names another repo.
- Do not use `C:\dev\RimBob` itself as the child agent's working directory.
- The child agent may do branch-local git work: inspect status, stage its own files, and commit coherent implementation slices on the delegated branch.
- Do not land or delete the worktree by default. Resumability matters more than tidiness until the user asks to close out.
- The child agent must not land to `master`, delete worktrees, or touch other branches unless the prompt explicitly asks for closeout or landing.
- Do not assume a run is safe to land because Codex exited successfully. Inspect the diff, verify behavior, and preserve unrelated work.
- If closeout is requested, prefer the repo's normal closeout discipline: narrow commits, sync with `master`, verify, land on the real `C:\dev\RimBob` `master`, then remove the worktree and branch only after the work is durable.

## Helper script

Use the bundled helper when possible:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.agents\skills\run-prompt-with-codex\scripts\Invoke-CodexPromptRun.ps1
```

It stores run records under:

```text
C:\Users\<user>\.codex\prompt-runs\<run-id>\
```

and worktrees under:

```text
C:\Users\<user>\.codex\worktrees\prompt-runs\<run-id>\RimBob
```

Each run record contains the prompt, JSONL event stream, final message, metadata, branch, worktree path, and parsed session id when available.

## Start a run

1. Read `AGENTS.md`, `Docs/DESIGN.md`, `HumanTodo.md`, and any focus doc relevant to the prompt.
2. Check the real checkout state before creating a child worktree:
   ```powershell
   git -C C:\dev\RimBob status --short --branch --untracked-files=all
   git -C C:\dev\RimBob worktree list --porcelain
   ```
3. Start the delegated run:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.agents\skills\run-prompt-with-codex\scripts\Invoke-CodexPromptRun.ps1 `
     -Mode Start `
     -Name "short-task-name" `
     -Prompt "Do the requested task. You may make branch-local commits for coherent implementation slices. Do not land to master, delete worktrees, or touch other branches unless explicitly requested."
   ```
   The helper passes `-m gpt-5.5 -c model_reasoning_effort="xhigh"` to `codex.cmd exec` by default.
4. Read the printed run id, session id, branch, worktree, final message path, and event log path.
5. Report the run id to the user if they may want to resume it later.

## Resume a run

Use the exact run id when known:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.agents\skills\run-prompt-with-codex\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Resume `
  -RunId "<run-id>" `
  -Prompt "Continue from the previous run. First inspect current git status and summarize what remains."
```

If the user wants to resume interactively in the CLI, use the recorded `session_id` from `metadata.json`:

```powershell
codex.cmd resume --include-non-interactive -m gpt-5.5 -c 'model_reasoning_effort="xhigh"' <session-id>
```

If the worktree was already deleted, say that repository-local continuation is no longer available. The CLI conversation may still be viewable/resumable, but the original cwd and files are gone.

## Inspect a run

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.agents\skills\run-prompt-with-codex\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode Show `
  -RunId "<run-id>"
```

Then inspect:

- `metadata.json`
- the latest `final-message-*.md`
- the latest `events-*.jsonl`
- `git -C <worktree> status --short --branch --untracked-files=all`
- `git -C <worktree> diff --stat`

## Optional closeout

Only close out when the user asks for it or the parent task clearly includes landing.

Preferred flow:

1. Inspect the run record and child worktree.
2. Review the actual diff. Identify session-owned files and unrelated/user-owned files.
3. Commit only coherent session-owned slices on the child branch.
4. Run relevant verification from the child worktree after syncing with master:
   ```powershell
   git -C <worktree> merge master
   ```
5. Land on the real `C:\dev\RimBob` `master` only when safe.
6. Remove the worktree and delete the branch only after the landed commit is verified.

The helper can perform the final land/remove step only for an already-clean, already-verified branch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.agents\skills\run-prompt-with-codex\scripts\Invoke-CodexPromptRun.ps1 `
  -Mode CloseOut `
  -RunId "<run-id>" `
  -LandAndClose `
  -Verified
```

This path requires:

- child worktree clean
- child branch already synced with `master`
- real `C:\dev\RimBob` on `master`
- real `C:\dev\RimBob` clean
- branch ahead of `master`
- explicit `-Verified`

It squash-lands the child branch, records the landed commit in metadata, removes the child worktree, and deletes the branch. If any guard fails, stop and handle the closeout manually.

## Prompting guidance

For delegated implementation prompts, include:

- the exact user request
- repo root and branch/worktree expectations
- which docs/plans to read first
- commit/closeout boundaries: branch-local commits are allowed for implementation; master landing and worktree deletion require explicit closeout/landing permission
- verification expectations
- strongest-model expectation: `gpt-5.5` with extra-high reasoning unless the user explicitly requested otherwise
- a requirement to leave a concise final message with changed files, tests, commit hashes, and blockers

For exploratory prompts, tell the child not to edit files unless explicitly asked.

For closeout prompts, tell the child to preserve unrelated dirty work and avoid broad staging.
