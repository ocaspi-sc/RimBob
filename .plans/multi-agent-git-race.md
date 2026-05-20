# Multi-Agent Git Race: Cross-Session Staged-Commit Contamination on `master`

Status: proposal / findings
Owner: TBD
Created: 2026-05-19

---

## 1. Incident summary

Two distinct failure modes surfaced during a `HumanTodo.md` cleanup session with multiple concurrent AI agents writing to the main checkout (`C:\dev\RimBob`) on `master`:

1. **Stale-file guard tripped 3× on `HumanTodo.md`.** Other sessions appended `/todo`
   entries to the same file in between this session's Read and Edit calls. Harmless
   but noisy; just contention on a shared inbox file.
2. **Cross-agent staged-commit contamination.** A session ran two `git mv` and two
   `git rm` operations against plan files, but did not immediately commit:
   - `git mv .plans/recommended-refactorings.md Docs/plans/recommended-refactorings.md`
   - `git mv .plans/deterministic-crop-yield-math.md Docs/plans/deterministic-crop-yield-math.md`
   - `git rm Docs/plans/food-advice-alerts-tightening.md`
   - `git rm Docs/plans/mark-harvest-unforbid.md`

   A parallel session then made its own unrelated edits and committed them. Its
   commit swept the four staged moves/deletes into a single commit titled
   `Update AGENTS.md` (`d620c4a`). Evidence:

   ```
   commit d620c4a Update AGENTS.md
     AGENTS.md                                              | -1
     {.plans => Docs/plans}/deterministic-crop-yield-math.md| 0   (rename)
     Docs/plans/food-advice-alerts-tightening.md            | -176 (delete)
     Docs/plans/mark-harvest-unforbid.md                    | -166 (delete)
     {.plans => Docs/plans}/recommended-refactorings.md     | 0   (rename)
   ```

   The author of `d620c4a` did not intend to land those plan moves/deletes; they
   were the previous session's half-finished work picked up by a broad `git add`
   or equivalent.

This violates the AGENTS.md invariant:
> *"The only unstaged changes on `C:\dev\RimBob` should be manual edits by the human."*

…and effectively bypasses agent attribution. A commit titled "Update AGENTS.md"
silently destroyed two plan files and relocated two more — anyone reading
`git log --stat` for the touched files will be misled about who, when, and why.

---

## 2. What the skills actually say

Grep across `.claude/`, `.agents/`, `Docs/`, and scripts shows **no skill uses
broad staging commands** (`git add -A`, `git add .`, `git add --all`, `git commit -a`).
The contamination came from an *agent improvising* git steps that the skill
text doesn't spell out, not from a skill explicitly telling it to do so.

### `/todo` skill (`.agents/skills/todo/SKILL.md`)

Step 4 already includes solid pre-flight checks:

- Refuses to commit if branch ≠ `master`.
- Refuses if `.git/index.lock` exists.
- Refuses if `git status --short` shows staged changes, unrelated working-tree
  changes, or signs of an active session.
- Refuses if `git diff -- HumanTodo.md` already has unrelated edits.
- Says "Stage only `HumanTodo.md` changes that belong to this `/todo` entry."
- Says do not retry on `git add`/`git commit` failure.

**Gaps in the skill text:**

- The exact command is not pinned. An agent could reasonably write
  `git add HumanTodo.md` (good) or `git add .` while in the dir (bad). The skill
  should mandate `git add -- HumanTodo.md`.
- There is no *post-stage / pre-commit* verification step. After staging, the
  agent should run `git diff --cached --name-only` and refuse to commit unless
  the output is exactly `HumanTodo.md`. Without this, if anything else slipped
  into the index between the pre-flight check and the stage, it still gets
  committed.
- The "Captured by /todo" section is a single high-contention line target. Two
  agents inserting near `<!-- entries go here -->` at the same time will collide
  on every concurrent run (the stale-file guard symptom from incident #1).

### `close-out-session` skill (`.agents/skills/close-out-session/SKILL.md`)

Strong English-language guardrails:
> *"Stage exact paths or hunks. Never use broad staging when unrelated changes are present."*
> *"If a file contains mixed session-owned and user-owned edits, inspect the diff carefully and stage only safe hunks."*

These are correct rules but are *agent-trusted*, not mechanically enforced. The
skill also doesn't require the post-stage `git diff --cached --name-only`
verification before each commit.

### Other skills / hooks

- `run-rimbob`, `minister-refine`, `run-minister-using-subagent`,
  `inspect-repo-for-idea`: none touch git staging.
- `.claude/hooks/`: empty. No pre-commit / pre-tool-use hook exists.
- `.claude/settings*.json`: no git-related guards.

### AGENTS.md

Has the rule "wait briefly and retry the narrow operation; do not force broad
Git actions" but it triggers on `index.lock` / observable conflicts, not on the
much more common case of *leftover staged state from a sibling session*.

---

## 3. Root cause

The repo's multi-agent invariant is enforced only by prose in three different
skill files plus `AGENTS.md`. There is **no mechanical fence** that:

1. Detects staged-but-uncommitted state at session start.
2. Prevents a commit whose staged set exceeds what the current task touched.
3. Prevents an agent from leaving staged state between tool calls (the
   load-bearing precondition for the bug).
4. Serializes destructive multi-step git operations on the shared main checkout.

So the failure mode is: Agent A runs `git mv` / `git rm` (which auto-stage),
gets interrupted or distracted before committing, leaves the index dirty.
Agent B at any later moment finds the index dirty, runs its own narrow
`git add <its files>` + `git commit`, and the prior-staged entries ride along.

The `/todo` skill avoids causing this, but cannot detect or prevent another
agent doing so — its pre-flight only protects the `/todo` write itself.

Secondary causes:
- Plans / multi-step refactors run on the **main checkout** instead of a worktree,
  in violation of `AGENTS.md` *GIT → "When changing code, …worktree + feature branch"*
  (the `git mv`/`git rm` of plan files is exactly the kind of multi-op work that
  should not have happened on `C:\dev\RimBob` directly).
- `git mv` and `git rm` **stage as a side effect**. Many agents reason about
  them as "rename a file" without registering they have just modified the index.

---

## 4. Remediation proposal

All four items below are doc / convention changes (no code, no git config, no
history rewrite). They target the agent-discipline contract first, then add a
mechanical fence as a backstop.

### 4.1. Stage → verify → commit in a single tool call (universal)

Add to `AGENTS.md` → GIT section:

> **Atomic stage+commit on `master`.** Any sequence of `git mv`, `git rm`,
> `git add`, or other index-mutating commands MUST be followed in the **same
> tool invocation** by a `git commit` that lands exactly the intended files.
> Never leave staged changes in the index across two tool calls on the main
> checkout. If you cannot commit immediately, do not stage.

This is the single highest-leverage rule; it makes the failure window
microscopic.

### 4.2. Mandatory pre-commit manifest check

Add to `AGENTS.md` → GIT section, and reference from `close-out-session` and
`/todo`:

> **Verify staged set before every commit.** Before every `git commit` on
> `master`, run `git diff --cached --name-only` and confirm the output equals
> the intended file set for this commit. If unexpected paths appear, do NOT
> commit. Either `git restore --staged -- <unexpected-path>` to drop them, or
> stop and report; never commit a superset of your intent.

### 4.3. Banned commands on the main checkout

Add to `AGENTS.md` → GIT section:

> **Banned on `C:\dev\RimBob` (the main checkout):**
> `git add -A`, `git add .`, `git add --all`, `git add -u`, `git commit -a`,
> `git commit -am ...`.
> Stage only by explicit path: `git add -- <path> [<path> ...]`. Worktrees can
> relax this if their working tree is known to contain only session-owned files.

(Grep confirms none of the skills use these today — this is a guard against
ad-hoc agent behavior, not a known offender.)

### 4.4. Tighten the `/todo` skill

Patch `.agents/skills/todo/SKILL.md` step 4:

- Replace "Stage only `HumanTodo.md` changes…" with the literal command:
  `git add -- HumanTodo.md`.
- Insert a post-stage check before the commit:
  `git diff --cached --name-only` MUST output exactly `HumanTodo.md`; otherwise
  unstage with `git restore --staged -- :/` and leave the entry uncommitted with
  a report.
- Add an explicit note: if `git diff --cached --name-only` shows files that are
  not `HumanTodo.md` *and were not staged by this skill*, the skill must NOT
  unstage them blindly (they may be load-bearing for another session); it must
  abort and report so the human can adjudicate.

### 4.5. Master-write advisory lock (lightweight)

`AGENTS.md` already has the wait-and-retry rule for `.git/index.lock`. That is
necessary but not sufficient because `index.lock` exists only during the
~milliseconds of a git plumbing operation, not across an agent's "do work then
commit" window.

Proposal: add an *advisory* lock convention for the main checkout only.

- File: `C:\dev\RimBob\.git\rimbob-master.lock` (already inside `.git/`, so
  gitignored by definition).
- Contents: one JSON line with `pid`, `agent` (e.g. `claude-code`, `codex`,
  `cursor`), `started_at` (ISO-8601 UTC), `intent` (short string, e.g.
  `plan-file-moves`, `close-out-session`).
- Acquire: write the file with `New-Item` only if it does not exist (or is
  older than a stale threshold, e.g. 10 minutes). Refuse to start
  index-mutating ops on `master` until acquired.
- Release: delete the file as the last step after `git commit` succeeds and
  `git status --short` is clean.
- Stale handling: a lock file older than 10 minutes is considered abandoned and
  may be replaced; the replacing agent must log the takeover.

This is intentionally advisory — agents must cooperate — but combined with
4.1 (atomic stage+commit) the lock window shrinks to a single tool call, which
is rarely contended.

### 4.6. Prefer worktrees for multi-step git work

Restate in `AGENTS.md`:

> **Multi-step git operations on the main checkout are forbidden.** If a task
> needs more than one of `git mv`, `git rm`, or staged edits across multiple
> files that are not all going into one immediate commit, do it in a worktree.
> Land via squash-merge to `master` as a single commit when the slice is green.

The `d620c4a` incident is a direct consequence of running `git mv .plans/…`
on `C:\dev\RimBob` instead of in a worktree.

### 4.7. Cleanup-on-entry expectation

Add to `close-out-session` and as a startup nudge for any agent that lands on
`master`:

> **On entry to a session on `C:\dev\RimBob`, run `git status --short` first.
> If the index is not clean and you did not stage it yourself, do NOT
> `git add` or `git commit` anything. Report the foreign staged paths and ask
> the human to adjudicate.** Never auto-unstage someone else's work.

This is the single rule that would have prevented `d620c4a` even if all other
rules were ignored.

---

## 5. Open questions

- **Mechanical enforcement vs. doc-only.** A `pre-commit` hook under
  `.git/hooks/pre-commit` could reject any commit on `master` where
  `git diff --cached --name-only` outputs paths that are not in a per-session
  manifest. But `.git/hooks` is not checked in, so each clone/worktree would
  need a one-time install. Worth doing as a follow-up plan.
- **Lock granularity.** A single `master` lock is coarse but matches the actual
  contention pattern (one shared checkout). Per-file locks would be more
  flexible but vastly more complex; reject for now.
- **Detecting the symptom retroactively.** Could a daily script flag commits
  whose title (`Update X`) does not match the changed paths, as a smell? Cheap
  to add, useful even if remediation 4.1–4.7 lands.
- ~~CLAUDE.md vs. AGENTS.md disagreement on plan location.~~ Resolved
  2026-05-20: `.plans/` is the canonical location. `CLAUDE.md` and `AGENTS.md`
  both point at `.plans/`, and `HumanTodo.md` links have been retargeted.
  The `d620c4a` rename direction (`.plans → Docs/plans`) was therefore wrong;
  that contributed to why the contamination was hard to spot. A stale
  duplicate `Docs/plans/rimapi-blueprint-placement-endpoint.md` is the only
  leftover and is filed as a follow-up in §7.

---

## 6. Acceptance / done criteria

- `AGENTS.md` GIT section updated with rules 4.1, 4.2, 4.3, 4.5 (lock), 4.6,
  4.7.
- `.agents/skills/todo/SKILL.md` step 4 patched per 4.4, and Claude-side
  pointer file (`.claude/skills/todo/SKILL.md`) left as-is (it just points at
  the canonical file).
- `.agents/skills/close-out-session/SKILL.md` step 4 reinforced with the
  pre-commit manifest check.
- No git config change, no history rewrite, no banned-tool wrapper code in
  this slice. Optional pre-commit hook is filed as a follow-up.

## 7. Out of scope (follow-ups worth filing)

- Pre-commit hook implementation + install script.
- Daily integrity sweep for "title vs. paths" commit smell.
- Delete the leftover stale `Docs/plans/rimapi-blueprint-placement-endpoint.md`
  (the canonical copy lives at `.plans/rimapi-blueprint-placement-endpoint.md`).
  Verify no other plan file is still duplicated under `Docs/plans/` or
  `Docs/Plans/` before removing the empty directory.
- Splitting `HumanTodo.md` "Captured by /todo" into per-agent append files to
  remove the high-contention insertion point (separate todo; would also fix
  incident #1 fully).
