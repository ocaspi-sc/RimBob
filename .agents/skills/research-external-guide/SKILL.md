---
name: research-external-guide
description: Research external web guides, articles, wiki pages, videos, tools, or community posts and convert them into RimBob guide-corpus Markdown with source-attributed local images. Use when asked to search online for game guides, build a durable Docs/guides corpus, save example images, compare external guide advice, or turn community layout/strategy guidance into RimBob-ready notes.
---

# Research External Guide

Turn external guides into a durable RimBob guide corpus. Default output is summarized Markdown plus local images, not copied articles.

## Core Rules

- Browse/search online when the user asks for current or external guides.
- Create one Markdown file per source unless the user asks for a combined report.
- Paraphrase and summarize. Do not paste full articles or long copied passages.
- Save useful source images locally when requested, and link them from the Markdown.
- Record source URL, retrieval date, author/site when known, license/reuse status, and limitations.
- Prefer `Docs/guides/<topic>/` for living guide-corpus files unless the user gives an exact path. On Windows, `Docs/Guides` and `Docs/guides` are the same directory.
- Keep generated guide docs separate from design docs. Update design docs only if the user makes a design decision.
- If the user asks to land, use repo git discipline: worktree, sync with `master`, staged manifest, guarded master commit, preserve unrelated dirt.

## Workflow

1. Ground the target.
   - Read `Docs/DESIGN.md`, `HumanTodo.md`, and the relevant focus doc if the guide will inform a minister, dashboard, RAG, or planning direction.
   - Identify the durable target path. Examples:
     - Base layouts: `Docs/guides/Base/`
     - Beginner survival: `Docs/guides/beginner/`
     - Food chain: `Docs/guides/food/`
   - If a matching corpus already exists, inspect it before adding duplicates.

2. Collect sources.
   - Search broadly first, then prefer primary or mechanics-stable sources: official wiki pages, maintained docs, major community guides, tools/mod pages, and reputable videos.
   - Keep a short source list with source URL, title, author/site, retrieval date, and access status.
   - If a source is blocked, removed, video-only, or has no accessible images, say so in the source Markdown instead of inventing content.

3. Use subagents only when allowed.
   - If the user explicitly asks for subagents, spawn one bounded research subagent per source or source cluster.
   - Ask each subagent for: title, source URL, 8-12 takeaways in original words, candidate image URLs, suggested local filenames, and licensing/attribution cautions.
   - Respect thread limits. Run sources in batches and close completed agents.
   - If subagents are not explicitly requested or not available, do the same extraction inline.

4. Download images.
   - Save images under `<target>/images/` with stable kebab-case filenames prefixed by source/site when useful.
   - Keep only images that support actual guide examples, layout patterns, UI workflows, or mechanics explanation.
   - Use direct image URLs from the source page when possible.
   - If shell network is blocked, rerun the same download command with escalation.
   - Do not create placeholder images for sources without usable image assets.

5. Write each Markdown file.
   - Use this shape:
     ```md
     # Source Title

     Source: https://...
     Retrieved: YYYY-MM-DD
     Source license: ...
     Attribution: ...
     Note: Original RimBob summary. This is not a verbatim copy of the source.

     ## RimBob Use
     ...

     ## Layout/Strategy/Workflow Heuristics
     - ...

     ## Images
     ![short alt](images/local-file.png)

     Source image: https://...

     ## Caution
     ...
     ```
   - Use section names that match the domain: `Layout Heuristics`, `Defense Heuristics`, `Workflow Heuristics`, `Mechanics Notes`, etc.
   - Keep bullets action-oriented and inspectable. Avoid generic advice that cannot help a minister or dashboard surface.

6. Attribution and copyright.
   - Wiki pages may have explicit licenses; record them.
   - Community articles, Steam pages, YouTube thumbnails, and screenshots often have no permissive reuse license. Treat them as local, source-attributed reference material unless permission is clear.
   - Store original image URLs beside local links so provenance survives.
   - If the user asks for public redistribution, re-check licenses before including non-permissive images.

7. Validate before closeout.
   - Check every Markdown local image reference exists.
   - Check no downloaded image is zero bytes.
   - Check `git status --short -uall <target>` so the changed set is explicit.
   - For docs-only guide corpus work, no build is needed unless code or generated site output changed.
   - Report exact file links and any source limitations.

## RimBob Transfer Lens

When extracting insights, classify them by where RimBob can use them:

- `Construction`: room adjacency, traffic, expansion, power, temperature, storage, planning layers.
- `Food`: farm/freezer/kitchen/dining loops, spoilage, cleanliness, storage access.
- `Defense`: chokepoints, cover, killbox geometry, fallback rooms, protected infrastructure.
- `Welfare`: bedrooms, dining/rec impressiveness, beauty, cleanliness, outdoors/indoors.
- `Medical`: hospital cleanliness, medicine access, bed reachability, prison/hospital proximity.
- `Dashboard`: evidence panels, source links, image-backed examples, timing/metadata.
- `RAG`: guide Markdown under `Docs/guides/**/*.md`, concise summaries, stable citations.

Prefer source-truth facts and visible examples over abstract "best practices."

