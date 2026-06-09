---
name: todo
description: Invoked when the user types exactly "/todo". Appends one short identified, tagged entry to the "Captured by /todo" section in Tasks.md. Plain captures stay line-only; discussion-backed captures may create and link a `.plans/` plan.
---

# /todo Skill

Append one identified, tagged line to `## Captured by /todo` in `Tasks.md`. Most captures = one line. If capture overlaps existing `.plans/` file (usually current session work), add one-two concrete anchors — files, tests, identifiers, commit, decision — and link that plan, not a bare line. Create a *new* `.plans/` file only when `/todo` refers to a slice discussed at length that no existing plan covers. Then commit touched files to `master` only when repo idle and commit easy; else leave uncommitted, say can commit later.

Low-context by default. Self-contained `/todo X` unrelated to session: use `X` directly, do not scan conversation. Only when topic clearly overlaps active session or existing `.plans/` file — even if args read self-contained — spend a little context to add a couple specifics + the plan link, not a full conversation scan. Unsure: prefer enriching — a richer line keeps the thread, beats a bare one that loses it.

## Steps

1. **Classify the capture**:
   - **Line-only** (default): self-contained, random, or small captures like `/todo add a dark mode toggle`.
   - **Link existing plan**: topic overlaps current session or an active `.plans/` file (even when args read self-contained). Add one-two concrete anchors from recent context — files, identifiers, test names, commit, or the decision just made — and link that plan. Do not create a new one.
   - **New plan**: `/todo` refers to current discussion ("that approach", "the design above") or asks to save a plan, *and* no existing plan covers it. Write a new `.plans/` file — not merely because text sounds non-trivial.

2. **Derive the entry content** from either:
   - Args passed after `/todo` (if any). Normal path.
   - No args: use only the immediately preceding user idea or task. Do not scan the whole conversation.
   - Linking or creating a plan: inspect only enough recent discussion to preserve the actual decisions, constraints, anchors, next steps.

3. **Pick tags** - one or more short hashtags classifying the entry. Choose from:
   | Tag | Meaning |
   |---|---|
   | `#idea` | Exploratory, not committed |
   | `#spike` | Needs investigation before committing |
   | `#ux` | Dashboard / frontend |
   | `#backend` | C# / .NET / API |
   | `#llm` | Prompt, model, LLM call |
   | `#rules` | Minister rules / lenses |
   | `#test` | Tests or fixtures |
   | `#doc` | Design doc update |
   | `#skill` | Codex skill or workflow |
   | `#m<N>` | Milestone target (e.g. `#m2`, `#m3`) |
   | `#debt` | Tech debt / refactor |
   | `#question` | Open question, needs decision |
   Pick the two or three most relevant. Combine freely.

4. **Pick an identifier** - one short, unique, one-word identifier for the line.
   - Lowercase kebab-case, e.g. `food-audit`, `dashboard-tabs`, or `rimapi-forage`.
   - Put the identifier immediately after the checkbox.
   - Check existing checkbox identifiers in `Tasks.md` before writing. If the obvious identifier already exists, add a short differentiator or number suffix.

5. **Link or create a plan** (skip in line-only mode):
   - First check `.plans/` for a file already covering the topic — the active session's plan is the likeliest match. If one fits, link it and do not create a new plan.
   - Else write a new `.plans/<unique-id>.md`: concise, execution-oriented — goal, current decision/context, implementation slices, validation, open questions or dependencies. Use local file links when useful, but do not over-spec signatures or DTO details that code owns. If that identifier already exists, reuse it when it matches or pick a distinct one.

6. **Append to `Tasks.md`** - insert the new line just after the `<!-- entries go here -->` comment in the `## Captured by /todo` section. Always one line; append the `[plan]` link whenever a plan is linked or created:
   ```md
   - [ ] unique-id [YYYY-MM-DD] #tag1 #tag2 Short description (name the anchoring files/tests/commit when enriching). [plan](.plans/unique-id.md)
   ```
   Use today's date from the current date context if available.

7. **Opportunistically commit only when easy**:
   - Check `git branch --show-current`. If not `master`, do not switch branches silently; leave entry uncommitted, report `/todo` needs `master` before auto-commit.
   - Check `.git/index.lock`. If it exists, leave entry uncommitted, report Git locked.
   - Check `git status --short` before staging. If there are staged changes, unrelated working-tree changes, or any other sign of an active session writing files, leave entry uncommitted, report can commit later.
   - Check `git diff -- Tasks.md` and the plan file, if any, before staging. If either already has unrelated edits, leave entry uncommitted, report can commit later.
   - Stage only the touched files with explicit paths: `git add -- Tasks.md` plus `.plans/<unique-id>.md` when a plan was created.
   - Run `git diff --cached --name-only` before committing. Must output exactly `Tasks.md`, plus the plan file only when a new plan was created (linking an existing plan stages nothing extra).
   - If the staged set is not exactly the intended file set, do not commit. Unstage only this skill's staged paths with `git restore --staged -- <paths>` if needed, leave entry uncommitted, report the unexpected staged paths. Do not blindly unstage files that may belong to another session.
   - Commit with message `Capture todo: <short description>`.
   - If `git add` or `git commit` fails for any reason, do not retry or request escalation; leave entry uncommitted, report the failure briefly.

8. **Confirm** in one line what was added; include the commit hash or say it was left uncommitted. No more than one sentence.

## Rules

- Do not recreate `Docs/TODO.md` or root `todo.md`; `Tasks.md` is the single todo surface.
- Touch `.plans/` only outside line-only mode: link the matching existing plan, or create a new one only when none fits.
- Do not read or summarize the full `Tasks.md` except to check existing identifiers, existing matching plan links, and the narrow insertion area.
- Do not quote existing todo contents in the reply.
- Do not modify `Docs/ROADMAP.md` unless the user explicitly asks to change milestone order.
- Do not ask for confirmation before writing; write and report.
- Do not force auto-commit through a dirty repo, index lock, permission issue, or active working session. `/todo` capture is more important than committing immediately.
- Keep descriptions short (~12 words). An enriched line may run longer to hold the anchors and plan link, but stays on one line.
- Always anchor on the args: use them directly when self-contained; when they overlap the session or an existing plan, add the concrete details and link that plan.
