---
name: todo
description: Invoked when the user types exactly "/todo". Appends one short tagged entry to the "Captured by /todo" section in HumanTodo.md, then commits that new line to master. It never creates plan files.
---

# /todo Skill

Appends one tagged line to the `## Captured by /todo` section in `HumanTodo.md`, then commits that line immediately to `master`.

This skill is intentionally low-context. For `/todo X`, use `X` directly, write one short line, and do not inspect the broader conversation for plans or extra detail.

## Steps

1. **Derive the entry content** from either:
   - The args passed after `/todo` (if any). This is the normal path.
   - If no args were passed, use only the immediately preceding user idea or task. Do not scan the whole conversation.

2. **Pick tags** - one or more short hashtags that classify the entry. Choose from:
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

3. **Append to `HumanTodo.md`** - insert the new line just after the `<!-- entries go here -->` comment in the `## Captured by /todo` section, with this format:
   ```md
   - [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
   ```
   Use today's date from the current date context if available.

4. **Commit only the new todo line to `master`**:
   - Check `git branch --show-current`. If it is not `master`, do not switch branches silently; report that `/todo` needs `master` before it can auto-commit.
   - Check `git diff -- HumanTodo.md` before staging. If `HumanTodo.md` already has unrelated edits, use partial staging so only the new `/todo` line is staged.
   - Stage only `HumanTodo.md` changes that belong to this `/todo` entry. Do not stage unrelated files.
   - Commit immediately with message `Capture todo: <short description>`.
   - If `git add` or `git commit` is blocked by permissions or an index lock, request the required approval and retry once.

5. **Confirm** in one line what was added and include the commit hash. No more than one sentence.

## Rules

- Do not recreate `Docs/TODO.md` or root `todo.md`; `HumanTodo.md` is the single todo surface.
- Do not create or update files under `Docs/plans/`. `/todo` is short-line capture only.
- Do not read or summarize the full `HumanTodo.md` unless a tool requires a narrow context snippet for insertion.
- Do not quote existing todo contents in the reply.
- Do not modify `Docs/ROADMAP.md` unless the user explicitly asks to change milestone order.
- Do not ask for confirmation before writing; just write and report.
- Do not leave the new `/todo` entry uncommitted unless the commit failed; if it failed, say so explicitly.
- Keep the description on the todo line short (12 words or fewer).
- If the user passes args (e.g. `/todo add a dark mode toggle to dashboard`), use those args directly rather than inferring from context.
