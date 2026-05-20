---
name: close-out-session
description: Close out a RimBob/Codex work session. Use when the user asks to wrap up, close out, end the session, review and commit session work, land the current slice on master, add follow-up todos, clean temporary artifacts, stop session-owned processes, or close a disposable worktree.
---

# Close Out Session

Close the session by turning the work into durable artifacts: a short recap, narrowly scoped commits, useful follow-up todo entries, and a clean handoff state.

## Workflow

1. **Establish scope.**
   - Read `AGENTS.md`, `HumanTodo.md`, and any focus docs or plans touched during the session.
   - Inspect `git status --short --branch`, staged changes, unstaged changes, untracked files, recent commits, and the current branch.
   - Identify which changes belong to this session from the conversation, file mtimes only as supporting evidence, and the actual diffs.
   - Classify dirty files as `session-owned`, `pre-existing or user-owned`, or `uncertain`. Do not stage, revert, delete, or move files outside `session-owned` without explicit approval.

2. **Review session-owned changes.**
   - Review the actual diffs against the user's request, touched focus docs/plans, and repo conventions.
   - Look for correctness risks, missing or stale docs/tests, accidental scope creep, generated artifacts, mixed ownership, and over-complicated implementation.
   - Fix clear session-owned issues before committing. Capture non-blocking design concerns as follow-ups instead of redesigning during closeout.
   - If ownership or intent is uncertain, ask before staging or rewriting that work.

3. **Recap only what matters.**
   - Summarize what changed, what was verified, what remains risky, and any user-owned dirt that must be preserved.
   - Report outcomes, not transcripts. Avoid exact full commands or long output unless the user asked, reproduction requires it, or a subtle Git/runtime state needs proof.

4. **Commit only this session's slices.**
   - If there are session-owned edits, group them into coherent commits. Prefer small commits by behavioral slice, not one giant mixed commit.
   - Stage exact paths or hunks. Never use broad staging when unrelated changes are present.
   - If a file contains mixed session-owned and user-owned edits, inspect the diff carefully and stage only safe hunks. If hunk staging is not practical, ask before touching the file.
   - Before every commit, run `git diff --cached --name-only` and compare it with the intended file manifest for that commit. If unexpected paths are staged, do not commit; unstage only paths you just staged or stop and report.
   - Never commit a staged superset of the intended slice, even if the extra paths look harmless.
   - Run the relevant verification before each commit or before the commit series when that is more appropriate.
   - After each commit, inspect the new commit's stat, changed paths, whitespace check, and diff summary. Confirm the commit matches the intended slice and the user's request.

5. **Land the commits on `master`.**
   - The closeout target is `master` unless the user explicitly names another target.
   - If already on `master`, leave the commits there.
   - If on another branch, first make sure all session-owned changes are committed and unrelated dirty work will not be disturbed by switching.
   - Prefer cherry-picking the session commit range onto `master` over merging a work branch. Avoid merge commits unless the user asked for one.
   - If switching to `master` would overwrite or strand unrelated dirty work, stop and ask. Do not stash unrelated user work unless the user approves.
   - After landing, verify `master` contains the intended commits and report any branch/worktree that still holds unlanded work.

6. **Capture follow-ups.**
   - Suggest a short list of possible follow-ups based on real gaps found during the session: failed verification, missing docs, deferred cleanup, new risks, or good next slices.
   - Add only the best one to three follow-ups to `HumanTodo.md` under `## Captured by /todo`, immediately after `<!-- entries go here -->`.
   - Use the repo todo format:
     ```md
     - [ ] unique-id [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
     ```
   - Give each line a unique, one-word, lowercase kebab-case identifier immediately after the checkbox.
   - Keep each todo line short and actionable. Use tags from the `/todo` skill when possible.
   - Commit closeout todo entries as their own slice when they are session-owned and can be safely landed on `master`.

7. **Clean up session-owned runtime state.**
   - Stop dev servers, host processes, watchers, subagents, or background helpers started during this session when they are no longer needed.
   - Remove temporary files only when they were created by this session and are not useful evidence. Preserve logs, replay corpus records, diagnostics, and prompt dumps unless the user asked to remove them or they are clearly throwaway.
   - If a disposable git worktree was created for this session and all session commits are landed, remove that worktree only after verifying its absolute path and clean status. Never remove the main repo checkout.
   - Check final `git status --short --branch` and report remaining changes by ownership.

## Guardrails

- Do not claim a clean closeout if uncommitted or unlanded session-owned work remains.
- Do not revert, reset, delete, or overwrite user-owned changes.
- Do not silently switch branches when dirty state could be affected.
- Do not invent follow-ups to pad the todo list. No follow-up is better than noisy todo debt.
- Do not use this skill to redesign the finished work. Capture design concerns as follow-ups unless they block correctness.
- If Git is locked or another session is committing, wait briefly and retry the narrow Git operation. If it remains locked, report the blocker and leave a precise handoff.

## Final Response

Close with a compact outcome report, not a command log:
- Work reviewed and completed: one to three bullets.
- Commits: hash + subject + landing target. Omit if nothing was committed.
- Verification: concise outcomes such as `build passed`, `tests passed`, `host reachable`, or `not run`.
- Follow-ups, cleanup, and remaining dirty state: include only non-empty or risky items; separate session-owned, user-owned, and uncertain dirt when present.
- Next options: at most three useful suggestions or concrete questions. Prefer items that resolve blockers, choose between plausible next slices, or clarify ownership; omit filler if there is no useful next step.

Keep technical detail proportional. Include exact commands, full paths, or raw output only when they are needed to reproduce a failure, prove a subtle state, or answer a direct user request.
