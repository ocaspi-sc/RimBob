---
description: Capture one short idea or task into the "Captured by /todo" section in HumanTodo.md with tags. Never saves a plan.
allowed-tools: Read, Edit, Write
---

## Context

- Today's date: !`date /t`

## Your task

Append one new entry to `HumanTodo.md` in this project, just after the `<!-- entries go here -->` comment in the `## Captured by /todo` section.

**Input:** Use `$ARGUMENTS` directly if the user provided a description after `/todo`. Otherwise derive the task from only the immediately preceding user idea or decision. Do not inspect the broader conversation for plan material.

**Entry format:**
```md
- [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
```

**Tag vocabulary** - pick the 2-3 most relevant:

| Tag | Meaning |
|---|---|
| `#idea` | Exploratory, not committed |
| `#spike` | Needs investigation first |
| `#ux` | Dashboard / frontend |
| `#backend` | C# / .NET / API |
| `#llm` | Prompt, model, LLM call |
| `#rules` | Minister rules / lenses |
| `#test` | Tests or fixtures |
| `#doc` | Design doc update |
| `#skill` | Claude Code skill or workflow |
| `#m<N>` | Milestone target (e.g. `#m2`) |
| `#debt` | Tech debt / refactor |
| `#question` | Open question, needs decision |

**Constraints:**
- Keep the description 12 words or fewer.
- Do NOT recreate `Docs/TODO.md` or root `todo.md`.
- Do NOT create or update files under `Docs/plans/`.
- Do NOT read, paste, or summarize the full `HumanTodo.md`; use only the minimum file context needed to insert after the marker.
- Do NOT touch `Docs/ROADMAP.md` unless the user explicitly asks to change milestone order.
- Confirm in one sentence what was added.
