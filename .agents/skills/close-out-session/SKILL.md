---
name: close-out-session
description: Close out a RimAI/Codex work session. Use when the user asks to wrap up, close out, end the session, commit session work, land the current slice on master, add follow-up todos, clean temporary artifacts, stop session-owned processes, or close a disposable worktree.
---

# Close Out Session

Close the session by turning the work into durable artifacts: a short recap, narrowly scoped commits, useful follow-up todo entries, and a clean handoff state.

## Workflow

1. **Establish scope.**
   - Read `AGENTS.md`, `HumanTodo.md`, and any focus docs or plans touched during the session.
   - Inspect `git status --short --branch`, staged changes, unstaged changes, untracked files, recent commits, and the current branch.
   - Identify which changes belong to this session from the conversation, file mtimes only as supporting evidence, and the actual diffs.
   - Classify dirty files as `session-owned`, `pre-existing or user-owned`, or `uncertain`. Do not stage, revert, delete, or move files outside `session-owned` without explicit approval.

2. **Recap the work.**
   - Summarize what changed, what was verified, what remains risky, and any user-owned dirt that must be preserved.
   - Keep the recap factual. Mention commands and results that matter; do not dump full command output unless asked.

3. **Commit only this session's slices.**
   - If there are session-owned edits, group them into coherent commits. Prefer small commits by behavioral slice, not one giant mixed commit.
   - Stage exact paths or hunks. Never use broad staging when unrelated changes are present.
   - If a file contains mixed session-owned and user-owned edits, inspect the diff carefully and stage only safe hunks. If hunk staging is not practical, ask before touching the file.
   - Run the relevant verification before each commit or before the commit series when that is more appropriate.
   - After each commit, inspect `git show --stat`, `git show --name-status`, and `git show --check` for the new commit. Confirm the commit matches the intended slice.

4. **Land the commits on `master`.**
   - The closeout target is `master` unless the user explicitly names another target.
   - If already on `master`, leave the commits there.
   - If on another branch, first make sure all session-owned changes are committed and unrelated dirty work will not be disturbed by switching.
   - Prefer cherry-picking the session commit range onto `master` over merging a work branch. Avoid merge commits unless the user asked for one.
   - If switching to `master` would overwrite or strand unrelated dirty work, stop and ask. Do not stash unrelated user work unless the user approves.
   - After landing, verify `master` contains the intended commits and report any branch/worktree that still holds unlanded work.

5. **Capture follow-ups.**
   - Suggest a short list of possible follow-ups based on real gaps found during the session: failed verification, missing docs, deferred cleanup, new risks, or good next slices.
   - Add only the best one to three follow-ups to `HumanTodo.md` under `## Captured by /todo`, immediately after `<!-- entries go here -->`.
   - Use the repo todo format:
     ```md
     - [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
     ```
   - Keep each todo line short and actionable. Use tags from the `/todo` skill when possible.
   - Commit closeout todo entries as their own slice when they are session-owned and can be safely landed on `master`.

6. **Clean up session-owned runtime state.**
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

Close with:
- Recap of completed work.
- Commits landed on `master`, with hashes.
- Verification performed and any skipped verification.
- Follow-ups added to `HumanTodo.md`.
- Cleanup performed.
- Remaining dirty state, separated by session-owned, user-owned, and uncertain.
- Next options: a short list of suggestions or concrete questions for how to proceed. Prefer items that resolve blockers, choose between plausible next slices, or clarify ownership; omit filler if there is no useful next step.
