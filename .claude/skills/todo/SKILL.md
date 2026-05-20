---
name: todo
description: Invoked when the user types exactly "/todo". Appends one short identified, tagged entry to the "Captured by /todo" section in HumanTodo.md, then opportunistically commits it to master only when Git looks idle. It never creates plan files.
---

# todo

Canonical instructions live in the cross-agent source of truth:
`.agents/skills/todo/SKILL.md`.

**Read `.agents/skills/todo/SKILL.md` now and follow it exactly.** Every step,
rule, and the opportunistic-commit guardrail there apply unchanged. This file
exists only so Claude Code can discover the skill; do not improvise an
alternative procedure.
