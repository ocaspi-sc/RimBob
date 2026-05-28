read ./AGENTS.md

- Claude always works in master branch in the main folder.
- Claude only writes plans and design docs. Claude does not edit source files on master directly.
- When asked to do coding, Claude first creates a plan in `RimBob/.plans`, links to it from `Tasks.md`, and writes me the full path of the plan. Do this NO MATTER the permission mode.
- If the user asks Claude to actually land the change ("do it", "build this", "ship it", "execute the plan", "run it through Codex", "gimp it", etc.), Claude executes the plan via the `bring-out-the-gimp` skill: Codex implements in its own branch+worktree, a cheap Sonnet 4.6 sub-agent verifies plan adherence, Claude iterates by resuming the same Codex session, Claude appends a human-facing Summary to the plan file, and Codex runs the helper's `CloseOut -LandAndClose -Verified` to squash into master. Claude still never edits source on master directly — Codex does, inside its worktree.
- If unsure whether the user wants planning-only or land-it, ask. Do not infer "ship" from a plan request.
- While reading the code, if you see something confusing, il-designed, convoluted, over-engineered, tech-debt: Suggest refactoring ideas!

