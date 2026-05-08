# RimAI — Codex Instructions

This file is loaded by Codex at the start of every session. It defines how Codex should behave in this project.

---

## Design doc maintenance (IMPORTANT)

**Global rule: any time the design changes, update the docs in the same turn.** This includes new decisions, revised constraints, new conventions, new components, renamed concepts, or anything a future session would otherwise have to re-derive. Don't wait for a trigger word, don't ask permission, don't defer to "later" — update the most relevant doc immediately and confirm with a one-line note.

The trigger phrases below are reinforcements, not the only condition. If any of them appear in a design discussion, treat doc-update as mandatory:
- "remember"
- "always"
- "from now on"
- "going forward"
- "whenever"
- "note that"
- "make sure"
- "don't forget"

### Which doc to update

| Topic | Doc to update |
|---|---|
| High-level principles, design goals | `Docs/DESIGN.md` |
| Build order, milestones | `Docs/ROADMAP.md` |
| Actionable tasks | `Docs/TODO.md` |
| Minister shape, rules system, rule refinement | `Docs/design/ministers.md` |
| Advice schema, feedback lifecycle, autonomy dial | `Docs/design/advice.md` |
| Dashboard UI, HTTP+SSE contract, auth posture | `Docs/design/dashboard.md` |
| State store, briefings, aggregates | `Docs/design/state-store.md` |
| Mayor's Agenda: living plan, minister_direction, dashboard, API | `Docs/design/agenda.md` |
| Flags, inter-minister communication | `Docs/design/communication.md` |
| RAG, knowledge base | `Docs/design/rag.md` |
| Evaluation, improvement loop | `Docs/design/evaluation.md` |
| Code structure, stack, interfaces | `Docs/design/architecture.md` |
| RIMAPI endpoints, conventions, controller catalogue | [`Docs/design/rimapi.md`](Docs/design/RimAPI.md) |
| Specific minister | `Docs/design/ministers/<name>.md` |
| HTN, planning *(deferred — Auto epic)* | `Docs/design/planning.md` |
| Labor / assignment solver *(deferred — Auto epic)* | `Docs/design/ministers/labor.md` |

If a decision affects multiple docs, update the most specific one and add a cross-reference in `Docs/DESIGN.md`'s decision log.

**When creating a new design doc, also add a row to the routing table above** so future sessions can find it.

---

## Session discipline

- **At session start:** read `Docs/DESIGN.md`, `Docs/TODO.md`, and any doc relevant to the session's focus.
- **At session end:** offer to update `Docs/TODO.md` with any new tasks uncovered.
- **Agent plans location:** store agent-created plans in `/.plans`.
- **Design sessions:** focus is exploration and documentation. Don't write code unless asked.
- **Build sessions:** focus is implementation. Don't redesign unless a blocker is found.

---

## Code conventions

- Use `// TODO:` comments liberally in code files to mark known gaps, unverified field names, deferred writes, and anything that needs revisiting before the next slice ships. TODOs are the audit surface between slices.
- All projects use `.NET 9`, `C#` latest features.
- Async everywhere that touches I/O.
- No code in `RimAI.Core` that takes external dependencies — it's pure domain types.
- Every new minister gets its own directory under `Src/Cabinet/<Name>/`.
- `Rules.cs` is the first file in every minister directory. It must compile and have tests before the LLM is wired.
- Fixture JSON files live in `Src/Tests/<MinisterName>/Fixtures/`.
- avoid using var for types

---

## What RimAI is

An assisted-gameplay advisor for RimWorld. The human plays the colony; a cabinet of self-improving LLM ministers (led by the Mayor) sends suggestions to a React+TS dashboard. MVP is suggest-only; per-minister `Auto` graduations come later (M7+).

When in doubt about architecture, read `Docs/DESIGN.md` first.

---

## Important constraints

- **Suggest by default; autonomy is per-minister and opt-in.** MVP ships with every advisor in `Suggest`; only `Suggest` is honored. See `Docs/design/advice.md`.
- **Output is `AdviceItem`s, not actions.** Ministers do not call RIMAPI write endpoints in MVP.
- **No direct minister-to-minister communication.** All coordination is flags; CoS arbitrates; Mayor synthesises.
- **LLMs are called only on escalation.** Rules handle the common case.
- **v1: human approval required for rule promotion.** Auto-approve is a post-MVP feature.
- **Briefings are the quality lever.** Keep them tight (~500 tokens). Derived facts belong in the state store, not in the LLM prompt.
- **Player Accept / Dismiss / Modify is the primary refinement signal.** Implicit state-diff is fallback.
- **Dashboard is localhost-only.** Host binds `127.0.0.1`. Never `0.0.0.0`.
- **Deferred (Auto epic):** HTN planner, bulletin board, Labor solver, RIMAPI write coverage, "only Labor touches pawn allocation." Re-engaged at M7. Don't implement before then.
