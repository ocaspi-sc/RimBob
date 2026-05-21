# fix-gimp-codex-flags

## Motivation
The `bring-out-the-gimp` skill's PS1 helper sends incorrect `codex exec` flags, and uses `codex.cmd` while the new `codex` skill uses bare `codex`. Four bugs discovered by comparing against the new `codex` skill:

1. `--skip-git-repo-check` missing from Start and Resume — codex skill says always use it.
2. `--sandbox workspace-write --full-auto` missing from Start — without these Codex runs read-only and can't edit files.
3. Resume passes `-m $Model -c $reasoningConfigArg` — codex skill says no config flags on resume; the session already inherits them.
4. `Resolve-CodexCmd` looks up `codex.cmd`; SKILL.md says "Use `codex.cmd`" — codex skill uses bare `codex`; align both.

## Scope

**File 1:** `C:\dev\RimBob\.claude\skills\bring-out-the-gimp\scripts\Invoke-CodexPromptRun.ps1`

- **`Resolve-CodexCmd`** (line ~57): `Get-Command codex.cmd` → `Get-Command codex`; update error message accordingly.
- **Start mode** (line ~205): add `--skip-git-repo-check`, `--sandbox workspace-write`, `--full-auto` to the `Invoke-CodexExec` argument array.
- **Resume mode** (line ~269): add `--skip-git-repo-check`; remove `-m $Model -c $reasoningConfigArg` from the argument array.

**File 2:** `C:\dev\RimBob\.claude\skills\bring-out-the-gimp\SKILL.md`

- **Default posture section**: replace `"Use codex.cmd, not codex or codex.ps1."` with `"Use bare codex — no .cmd suffix."` (or just delete the line).

**Non-goals:**
- Do not add `2>$null` suppression — the script intentionally captures stderr via `2>&1 | Tee-Object` for session ID extraction and event logging.
- Do not change `--json` or `-o $lastMessagePath` — needed for final-message capture.
- Do not touch `$Model` / `$ReasoningEffort` parameters or metadata — still used for Start and stored for reference.

## Approach

### `Resolve-CodexCmd` fix (around line 57)

Current:
```powershell
$command = Get-Command codex.cmd -ErrorAction SilentlyContinue
if ($null -eq $command) {
    throw "codex.cmd was not found on PATH. Install/repair the Codex CLI before using this skill."
}
```

Replace with:
```powershell
$command = Get-Command codex -ErrorAction SilentlyContinue
if ($null -eq $command) {
    throw "codex was not found on PATH. Install/repair the Codex CLI before using this skill."
}
```

### Start mode fix (around line 205)

Current:
```powershell
$exitCode = Invoke-CodexExec -Arguments @("exec", "-C", $worktreePath, "--json", "-m", $Model, "-c", $reasoningConfigArg, "-o", $lastMessagePath, "-") -EventsPath $eventsPath -InputPath $promptPath
```

Replace with:
```powershell
$exitCode = Invoke-CodexExec -Arguments @("exec", "--skip-git-repo-check", "-C", $worktreePath, "--sandbox", "workspace-write", "--full-auto", "--json", "-m", $Model, "-c", $reasoningConfigArg, "-o", $lastMessagePath, "-") -EventsPath $eventsPath -InputPath $promptPath
```

### Resume mode fix (around line 269)

Current:
```powershell
$exitCode = Invoke-CodexExec -Arguments @("exec", "resume", "--json", "-m", $Model, "-c", $reasoningConfigArg, "-o", $lastMessagePath, $SessionId, "-") -EventsPath $eventsPath -InputPath $resumePromptPath
```

Replace with:
```powershell
$exitCode = Invoke-CodexExec -Arguments @("exec", "--skip-git-repo-check", "resume", "--json", "-o", $lastMessagePath, $SessionId, "-") -EventsPath $eventsPath -InputPath $resumePromptPath
```

### SKILL.md fix

In the **Default posture** section, change:
```
- Use `codex.cmd`, not `codex` or `codex.ps1`.
```
To:
```
- Use bare `codex` (no `.cmd` suffix) — consistent with the `codex` skill.
```

## Verification

- Read both changed files; confirm all four hunks landed.
- No build step needed (PS1 and MD are interpreted/text).
- Smoke: next real gimp run should no longer fail at the `codex not found` / `--sandbox` / git-repo-check stage.

---

## Summary (landed 2026-05-21)

**Motivation.** The gimp skill was built before the dedicated `codex` skill existed. Comparing the two revealed four flag bugs that would cause every Codex run to silently misbehave.

**Scope.**
- `Invoke-CodexPromptRun.ps1` `Resolve-CodexCmd`: `codex.cmd` → `codex`
- `Invoke-CodexPromptRun.ps1` Start: added `--skip-git-repo-check`, `--sandbox workspace-write`, `--full-auto`
- `Invoke-CodexPromptRun.ps1` Resume: added `--skip-git-repo-check`, removed `-m`/`-c` flags (session inherits them)
- `SKILL.md` Default posture: updated `codex.cmd` note to bare `codex`

**How to verify (human).**
- Commands: next `bring-out-the-gimp` run should start without `codex not found` / sandbox errors
- Files: `.claude/skills/bring-out-the-gimp/scripts/Invoke-CodexPromptRun.ps1`, `.claude/skills/bring-out-the-gimp/SKILL.md`

**Landed directly on master by Claude** (skill/config files, no worktree needed)
