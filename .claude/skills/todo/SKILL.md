---
name: todo
description: Invoked when the user types exactly "/todo". Captures the current idea, task, or plan from the conversation and appends a tagged entry to todo.md at the project root. If the discussion includes a plan (steps, design decisions, a technical approach), also saves it to Docs/plans/ and links to it from the entry.
version: 1.0.0
---

# /todo Skill

Appends a tagged line to `todo.md` (project root) from whatever the user just said or is currently discussing.

## Steps

1. **Derive the entry content** from either:
   - The args passed after `/todo` (if any), or
   - The most recent idea / decision / task discussed in the conversation.

2. **Pick tags** — one or more short hashtags that classify the entry. Choose from:
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
   | `#skill` | Claude Code skill or workflow |
   | `#m<N>` | Milestone target (e.g. `#m2`, `#m3`) |
   | `#debt` | Tech debt / refactor |
   | `#question` | Open question, needs decision |
   Pick the two or three most relevant. Combine freely.

3. **Check for a plan** — if the conversation includes a multi-step plan, design breakdown, or technical approach worth preserving:
   - Derive a short kebab-case filename from the topic (e.g. `feedback-loop-design.md`).
   - Write the plan to `Docs/plans/<filename>`.
   - Include a `[plan](Docs/plans/<filename>)` link in the todo line.

4. **Append to `todo.md`** — insert the new line just after the `<!-- entries go here -->` comment, with this format:
   ```
   - [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description. [plan](Docs/plans/filename.md)
   ```
   Omit the `[plan](…)` part if there is no plan.
   Use today's date (from the `currentDate` context if available).

5. **Confirm** in one line what was added (and the plan filename if one was saved). No more than two sentences.

## Rules

- Do not modify `Docs/TODO.md` or `Docs/ROADMAP.md` — those are milestone-tracked.
- Do not ask for confirmation before writing; just write and report.
- Keep the description on the todo line short (≤ 12 words). Depth goes in the plan doc.
- If the user passes args (e.g. `/todo add a dark mode toggle to dashboard`), use those args directly rather than inferring from context.
