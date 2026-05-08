---
description: Capture an idea or task into Human.md with tags. Optionally saves a plan to Docs/plans/ and links it.
allowed-tools: Read, Edit, Write
---

## Context

- Today's date: !`date /t`
- Current Human.md: !`type Human.md`

## Your task

Append one new entry to `Human.md` in this project, just after the `<!-- entries go here -->` comment.

**Input:** Use `$ARGUMENTS` if the user provided a description after `/todo`. Otherwise derive the task from the most recent idea or decision in the conversation.

**Entry format:**
```
- [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description.
```
Or, if a plan is being saved:
```
- [ ] [YYYY-MM-DD] #tag1 #tag2 Short imperative description. [plan](Docs/plans/filename.md)
```

**Tag vocabulary** — pick the 2–3 most relevant:

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

**Plan rule:** If the conversation contains a multi-step design approach or technical plan worth preserving, write it to `Docs/plans/<kebab-name>.md` and add the `[plan](…)` link. Otherwise omit the link entirely.

**Constraints:**
- Keep the description ≤ 12 words.
- Do NOT touch `Docs/TODO.md` or `Docs/ROADMAP.md`.
- Confirm in one sentence what was added (and plan filename if saved).
