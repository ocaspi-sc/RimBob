---
name: run-minister-using-subagent
description: Run a RimBob minister's LLM/manual fallback using a subagent instead of Gemini. Use when the user explicitly asks to generate minister output with a subagent (Codex subagent or Claude Code's Agent/Task tool), bypass Gemini quota/network failures, run the manual LLM step, or paste/ingest manually generated minister advice through `/api/ministers/{minister}/llm-output/manual`.
---

# run-minister-using-subagent

Canonical instructions live in the cross-agent source of truth:
`.agents/skills/run-minister-using-subagent/SKILL.md`.

**Read `.agents/skills/run-minister-using-subagent/SKILL.md` now and follow it
exactly.** In Claude Code, drive the subagent step with the Agent/Task tool
using a JSON-only sub-prompt. Every other workflow step, guardrail, and
reporting rule there applies unchanged. This file exists only so Claude Code
can discover the skill; do not improvise an alternative procedure.
