# RimAI — Human Todo

> Loose capture of future ideas, spikes, and "what if" tasks from the human.
> Anything serious that gets promoted belongs in `Docs/TODO.md` or `Docs/ROADMAP.md`.
> Add entries with `/todo`. Format: `- [ ] [date] #tag … description [plan](Docs/plans/…)`

---

Is the agenda saved to a file that the dashboard reads from? is there history?

Migrate from codex chrome plugin to playwright for frontend

Refine System prompts for agents - how?

Simplify AGENTS.md

## Fill out Guide corpus (download and create guides)

## Get Dashboard inspiration from existing mods

## some actions are very straighforward - we might be able to get away with adding them to execution layer already in the MVP:
- Mark nearby fully grown trees to cut down if we need wood
- Mark berries / animal to hunt
- Modify Priorities
- Basicially any simple action




## About the Food minister's Advice / Alerts:
- The first alert "Resource requests  attention  food" - wdym attention? why not Trade? 
- also it's bad advice - this is day one, i'm not going to be sending out carvans to traders?
- Too many things are generated. focus on the important + possible currently or short term. let the mayor worry about the grand strategy.
- Alerts priorities use numbers 1-10
- "Manage Food Stockpile" requested labor, why not tiles to be marked as storage (action)?
- In general, the most basic actions like hauling cleaning etc should be automated by the game, and only request labor for urgent cases.
- All of the LABOR ones just say "LABOR labor capacity " wdym labor capacity ? Also should specify what kind of skill is required for the task.
- "Wild harvest" one: it should say exactly where are the nearest edible plants, and suggest action to mark them for harvest.

## Dashboard v2: brainstorm session.
full redesign of the dashboard.
- left side: have a tab for each minister, with emojis.
- Add a SYSTEM tab for info about llm usage, logs, etc.
- Top of main area: bar of Tabs for our main views: System Prompt, Briefing, RAG, Rules, Advice.
Each view is structured into sections as relevant for each type, for readbility and ease of navigation.
- Remove The pushback buttons for now
- Right side is fine
- Header is fine
Read the design docs to see if i missed something important. do we have a place for triggers?

Then research either/or
- frontend dashboard skill for ai agents
- react library for dashboards with good support for collapsible panels and data

## Rename the whole project from RimAI to RimBob

<!-- entries go here -->
- [ ] [2026-05-09] #dashboard #ux Add button to dashboard "what was sent" / prompt-introspection screen that copies the full system + user prompt to the clipboard. Pairs with the manual-fallback flow (`logs/mayor-prompt-latest.md`, `POST /api/agenda/manual`) for when Gemini is rate-limited.
