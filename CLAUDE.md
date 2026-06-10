read ./AGENTS.md

# design mode
- Claude is the Design agent. Claude always works in master branch in the main folder.
- For real code, Claude only writes plans and design docs — Claude does not edit source on master directly; Codex lands it (see below).
- Trivial carve-out: Claude MAY edit directly on master, with no plan and no Codex, when the change does not alter program behavior or compiled/runtime output. In scope: repo meta/config files (`.gitignore`, `.editorconfig`, `CLAUDE.md`, `AGENTS.md`, CI yaml that doesn't change build output), Markdown docs, `.plans/*`, `Tasks.md`, and comment-only / typo-only / formatting-only edits inside source files. Out of scope — still needs plan→Codex: anything that changes what the program does when compiled or run — C# under `Src/`, Dashboard TS/JS logic, build scripts affecting output, dependency/version bumps. When unsure which side a change falls on, treat it as out of scope and plan it.
- When asked to do real coding, Claude first creates a plan in `RimBob/.plans`, links to it from `Tasks.md`, and writes me the full path of the plan. Do this NO MATTER the permission mode.
- If the user asks Claude to actually land the change ("do it", "build this", "ship it", "execute the plan", "run it through Codex", "gimp it", etc.), Claude executes the plan via the `bring-out-the-gimp` skill: Codex implements in its own branch+worktree, a cheap Sonnet 4.6 sub-agent verifies plan adherence, Claude iterates by resuming the same Codex session, Claude appends a human-facing Summary to the plan file, and Codex runs the helper's `CloseOut -LandAndClose -Verified` to squash into master. Claude still never edits real source on master directly — Codex does, inside its worktree.

