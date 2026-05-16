---
name: todo
description: Invoked when the user types exactly "/todo". Appends one short tagged entry to the "Captured by /todo" section in HumanTodo.md, then opportunistically commits it to master only when Git looks idle. It never creates plan files.
---

# /todo Skill

Appends one tagged line to the `## Captured by /todo` section in `HumanTodo.md`, then commits that line to `master` only when the repo looks idle and the commit is easy. If not, leave it uncommitted and report that it can be committed later.

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

4. **Opportunistically commit only when it is easy**:
   - Check `git branch --show-current`. If it is not `master`, do not switch branches silently; leave the entry uncommitted and report that `/todo` needs `master` before it can auto-commit.
   - Check `.git/index.lock`. If it exists, leave the entry uncommitted and report that Git is locked.
   - Check `git status --short` before staging. If there are staged changes, unrelated working-tree changes, or any other sign of an active working session writing files, leave the entry uncommitted and report that it can be committed later.
   - Check `git diff -- HumanTodo.md` before staging. If `HumanTodo.md` already has unrelated edits, leave the entry uncommitted and report that it can be committed later.
   - Stage only `HumanTodo.md` changes that belong to this `/todo` entry. Do not stage unrelated files.
   - Commit with message `Capture todo: <short description>`.
   - If `git add` or `git commit` fails for any reason, do not retry or request escalation; leave the entry uncommitted and report the failure briefly.

5. **Confirm** in one line what was added and either include the commit hash or say it was left uncommitted. No more than one sentence.

## Rules

- Do not recreate `Docs/TODO.md` or root `todo.md`; `HumanTodo.md` is the single todo surface.
- Do not create or update files under `Docs/plans/`. `/todo` is short-line capture only.
- Do not read or summarize the full `HumanTodo.md` unless a tool requires a narrow context snippet for insertion.
- Do not quote existing todo contents in the reply.
- Do not modify `Docs/ROADMAP.md` unless the user explicitly asks to change milestone order.
- Do not ask for confirmation before writing; just write and report.
- Do not force auto-commit through a dirty repo, index lock, permission issue, or active working session. `/todo` capture is more important than committing immediately.
- Keep the description on the todo line short (12 words or fewer).
- If the user passes args (e.g. `/todo add a dark mode toggle to dashboard`), use those args directly rather than inferring from context.
