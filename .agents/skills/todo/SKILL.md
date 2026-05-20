---
name: todo
description: Invoked when the user types exactly "/todo". Appends one short identified, tagged entry to the "Captured by /todo" section in HumanTodo.md. Plain captures stay line-only; discussion-backed captures may create and link a `.plans/` plan.
---

# /todo Skill

Appends one identified, tagged line to the `## Captured by /todo` section in `HumanTodo.md`. Plain captures stay line-only. If the `/todo` clearly refers to a design or implementation slice that was just discussed at length, create a concise `.plans/` file and link it from the todo line. Then commit the touched files to `master` only when the repo looks idle and the commit is easy. If not, leave them uncommitted and report that they can be committed later.

This skill is intentionally low-context by default. For a self-contained `/todo X`, use `X` directly, write one short line, and do not inspect the broader conversation for plans or extra detail. Become context-aware only when the command itself depends on the current discussion.

## Steps

1. **Classify the capture mode**:
   - **Line-only** is the default. Use it for self-contained, random, small, or explicit no-plan captures such as `/todo add a dark mode toggle`.
   - **Plan-backed** is for `/todo` commands that refer to the current discussion, e.g. "the thing we just discussed", "this plan", "that approach", "the design above", or when the user explicitly asks to make/save/link a plan.
   - Do not create a plan merely because the text sounds non-trivial. Create a plan when the todo would lose important context without the recent discussion or the user asked for one.

2. **Derive the entry content** from either:
   - The args passed after `/todo` (if any). This is the normal path.
   - If no args were passed, use only the immediately preceding user idea or task. Do not scan the whole conversation.
   - In plan-backed mode, inspect only enough recent discussion to preserve the actual decisions, constraints, and next implementation steps.

3. **Pick tags** - one or more short hashtags that classify the entry. Choose from:
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

4. **Pick an identifier** - choose one short, unique, one-word identifier for the line.
   - Use lowercase kebab-case, e.g. `food-audit`, `dashboard-tabs`, or `rimapi-forage`.
   - Put the identifier immediately after the checkbox.
   - Check existing checkbox identifiers in `HumanTodo.md` before writing. If the obvious identifier already exists, add a short differentiator or number suffix.

5. **Create a plan only in plan-backed mode**:
   - Write the plan to `.plans/<unique-id>.md`.
   - Keep it concise and execution-oriented: goal, current decision/context, implementation slices, validation, open questions or dependencies.
   - Use local file links when useful, but do not over-spec source signatures or DTO details that code owns.
   - If a plan file with the chosen identifier already exists, reuse or link that exact plan when it matches; otherwise choose a distinct identifier.

6. **Append to `HumanTodo.md`** - insert the new line just after the `<!-- entries go here -->` comment in the `## Captured by /todo` section, with this format:
   ```md
   - [ ] unique-id [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
   ```
   In plan-backed mode, append the plan link at the end:
   ```md
   - [ ] unique-id [YYYY-MM-DD] #tag1 #tag2 Short imperative description. [plan](.plans/unique-id.md)
   ```
   Use today's date from the current date context if available.

7. **Opportunistically commit only when it is easy**:
   - Check `git branch --show-current`. If it is not `master`, do not switch branches silently; leave the entry uncommitted and report that `/todo` needs `master` before it can auto-commit.
   - Check `.git/index.lock`. If it exists, leave the entry uncommitted and report that Git is locked.
   - Check `git status --short` before staging. If there are staged changes, unrelated working-tree changes, or any other sign of an active working session writing files, leave the entry uncommitted and report that it can be committed later.
   - Check `git diff -- HumanTodo.md` and the plan file, if any, before staging. If either already has unrelated edits, leave the entry uncommitted and report that it can be committed later.
   - Stage only the touched files with explicit paths: `git add -- HumanTodo.md` plus `.plans/<unique-id>.md` when a plan was created.
   - Run `git diff --cached --name-only` before committing. It must output exactly `HumanTodo.md` for line-only mode, or exactly `HumanTodo.md` plus the plan file for plan-backed mode.
   - If the staged set is not exactly the intended file set, do not commit. Unstage only this skill's staged paths with `git restore --staged -- <paths>` if needed, leave the entry uncommitted, and report the unexpected staged paths. Do not blindly unstage files that may belong to another session.
   - Commit with message `Capture todo: <short description>`.
   - If `git add` or `git commit` fails for any reason, do not retry or request escalation; leave the entry uncommitted and report the failure briefly.

8. **Confirm** in one line what was added and either include the commit hash or say it was left uncommitted. No more than one sentence.

## Rules

- Do not recreate `Docs/TODO.md` or root `todo.md`; `HumanTodo.md` is the single todo surface.
- Do not create or update files under `.plans/` in line-only mode.
- Do create and link a `.plans/` file in plan-backed mode.
- Do not read or summarize the full `HumanTodo.md` except to check existing identifiers, existing matching plan links, and the narrow insertion area.
- Do not quote existing todo contents in the reply.
- Do not modify `Docs/ROADMAP.md` unless the user explicitly asks to change milestone order.
- Do not ask for confirmation before writing; just write and report.
- Do not force auto-commit through a dirty repo, index lock, permission issue, or active working session. `/todo` capture is more important than committing immediately.
- Keep the description on the todo line short (12 words or fewer).
- If the user passes self-contained args (e.g. `/todo add a dark mode toggle to dashboard`), use those args directly rather than inferring from context.
