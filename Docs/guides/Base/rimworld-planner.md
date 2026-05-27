# RimWorld Planner

Source: https://www.matteobononi.it/rimworld_planner/  
Retrieved: 2026-05-21  
Source license: No explicit reuse license found  
Attribution: Matteo Bononi / RimWorld Planner  
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Planning workflow reference rather than a mechanics guide. Useful for future dashboard or Willie tooling: drafts, overlays, ASCII exchange, PNG exports, local-first maps, and fast iteration.

## Workflow Heuristics

- Planning should be offline-first and cheap to start. No account should be required for private drafts.
- Multiple maps/drafts matter because base layout is comparative: players need to duplicate, rename, and discard ideas.
- ASCII import/export is a useful bridge to the More Planning mod and to text-based agent workflows.
- Background-image overlays support tracing from in-game screenshots.
- Fast sketch tools are enough for many decisions: rectangles, circles, lines, eraser, pan, zoom, and WASD navigation.
- PNG export is useful for docs, design reviews, and before/after comparison.
- Gallery publishing should be opt-in so private working layouts do not become public artifacts.
- Map metadata such as title, description, tags, and published ID make layout reuse and search easier.
- Room generation is useful as a variant generator when the player wants irregular, mountain, or organic spaces.
- Generated shapes should round-trip back into the main planner instead of living in a separate tool silo.

## Images

The source does not expose static base-layout example images in the page. No local images were saved for this source.

## Dashboard/Agent Implications

- A future Willie dashboard could export a current layout sketch as PNG plus a compact text plan.
- A future RAG/corpus entry should prefer machine-readable adjacency facts over screenshots when RimBob is expected to reason over the plan.
- If RimBob ever suggests room placement, the UI should show it as an inspectable draft layer, not as an automatic build order.
