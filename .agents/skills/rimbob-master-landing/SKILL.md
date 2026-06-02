---
name: rimbob-master-landing
description: Safely land a verified RimBob feature branch or worktree slice onto the real `C:\dev\RimBob` `master` checkout. Use when the user says land, land it, close out to master, promote this slice, or asks to preserve dirty `Tasks.md` / `.plans` while committing an exact manifest with Host proof.
---

# RimBob Master Landing

Use this skill for real-master landings. It complements `close-out-session`: use `close-out-session` for the full recap/cleanup workflow, and use this skill's helper for the fragile Git operation.

## Helper

Prefer the repo helper:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\tools\land-rimbob-slice.ps1 `
  -FeatureRef <branch-or-commit> `
  -ManifestFile <absolute-or-repo-relative-file> `
  -CommitSubject "feat(scope): summary" `
  -CommitBody "Short why/contents." `
  -TaskId <task-id-if-Tasks.md-is-in-manifest> `
  -TaskAnchorId <insert-after-task-id> `
  -BuildDashboard `
  -RestartHost
```

The helper must be run from the real `C:\dev\RimBob` checkout. It acquires `.git\rimbob-master.lock`, refuses a dirty index, stages only the manifest, stages `Tasks.md` as a one-line task-id patch when requested, runs `git diff --cached --check`, commits, releases the lock, then optionally builds/tests/restarts Host and verifies health.

Use `-DryRun` first when the branch, manifest, or `Tasks.md` task id is uncertain. If the sandbox blocks Git metadata writes, rerun the exact same helper command with approval/escalation instead of rewriting the sequence manually.

## Workflow

1. Prepare the feature branch:
   - Commit the slice on its feature branch.
   - Merge current `master` into the feature branch before verification.
   - Run relevant verification in the feature worktree.
   - Build the exact manifest with `git diff --name-only master...HEAD`.

2. Classify main checkout dirt:
   - Check `git status --short --branch` in `C:\dev\RimBob`.
   - Treat `.plans/*`, `Tasks.md`, `AGENTS.md`, and docs dirt as user-owned unless it is part of the requested slice.
   - Never stage the whole dirty `Tasks.md` from main.

3. Land with the helper:
   - Pass the exact manifest file or explicit path list.
   - If `Tasks.md` is in the manifest, also pass `-TaskId`; the helper copies only that task line from the feature ref into both the staged blob and working tree.
   - Pass `-TaskAnchorId` when a new task line should be inserted after a known existing task.
   - Pass `-BuildDashboard` when `Dashboard/` changed.
   - Pass `-RestartHost` for runtime or dashboard changes that must be visible after landing.

4. If the helper fails:
   - Read its error before acting.
   - If it says a lock remains because staged changes remain, do not release the lock until the staged manifest is either committed or explicitly unwound.
   - Inspect `git diff --cached --name-only` and compare it with the intended manifest.
   - Do not run broad `git reset`, `git add -A`, or commit a staged superset.

5. Handoff:
   - Report commit hash and subject.
   - Report build/test results.
   - If Host was restarted, report `/api/health`, `/api/system/health`, `runtime_root`, `host_process_path`, and dashboard URL.
   - Report remaining main-checkout dirt by ownership.

## Notes

- The helper intentionally does not push.
- The helper intentionally leaves the lock in place after a post-staging failure, because that is safer than letting another agent commit a partial landing.
- For docs-only or skill-only landings, use `-SkipDotNetBuild -SkipTests` only when the change cannot affect compiled code or runtime behavior.
