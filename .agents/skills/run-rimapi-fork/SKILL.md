---
name: run-rimapi-fork
description: Build, deploy, restart, launch, or live-verify the RimBob RIMAPI fork at C:\dev\RIMAPI-for-RimBob. Use when the user asks to run/check/rebuild/deploy RIMAPI, restart RimWorld for RIMAPI, verify the loaded RIMAPI DLL, check /api/v1/version, check /api/v1/dev/endpoints, or prove a fork endpoint is live in RimWorld.
---

# run-rimapi-fork

Operate the sibling RIMAPI fork, not RimBob Host. RimBob talks to the mod over HTTP at `http://localhost:8765/`; fork code lives in `C:\dev\RIMAPI-for-RimBob` and targets RimWorld 1.6 only.

## Paths

- Fork repo: `C:\dev\RIMAPI-for-RimBob`
- Built DLL: `C:\dev\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll`
- Installed DLL: `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll`
- RimWorld exe: `D:\Games\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe`
- Base URL: `http://localhost:8765/api/v1`

## Workflow

1. Check the fork state before editing or building:
   ```powershell
   git -C C:\dev\RIMAPI-for-RimBob status --short
   git -C C:\dev\RIMAPI-for-RimBob branch --show-current
   git -C C:\dev\RIMAPI-for-RimBob log -1 --oneline
   ```

2. Build the supported mod target:
   ```powershell
   dotnet build C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj -c Release-1.6
   ```

3. Compare the built DLL with the installed DLL:
   ```powershell
   Get-FileHash -Algorithm SHA256 `
     C:\dev\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll, `
     D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll
   ```

4. If the hashes differ, deploy the rebuilt DLL. RimWorld does not hot-reload mod assemblies, so stop or close RimWorld before copying when the file is locked or when the user asked to restart/reload the mod:
   ```powershell
   Copy-Item -LiteralPath C:\dev\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll `
     -Destination D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll `
     -Force
   ```

5. Launch RimWorld when live proof is requested and it is not running:
   ```powershell
   Start-Process -FilePath D:\Games\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe `
     -WorkingDirectory D:\Games\SteamLibrary\steamapps\common\RimWorld
   ```

6. Prove the loaded DLL after RimWorld starts:
   ```powershell
   Invoke-RestMethod http://localhost:8765/api/v1/version | ConvertTo-Json -Depth 8
   Invoke-RestMethod http://localhost:8765/api/v1/dev/endpoints | ConvertTo-Json -Depth 8
   ```

## Proof Standard

- `/api/v1/version` is the strongest loaded-build proof. Report `version`, `build_commit_sha`, `build_number`, and `build_dirty`.
- `/api/v1/dev/endpoints` proves route registration. Report the endpoint count and whether the target route appears.
- `GET /api/v1/game/state` distinguishes "RIMAPI server loaded" from "a map is loaded"; `map_count = 0` means main menu/no map, not necessarily a fork failure.
- Hashes must match between built and installed DLLs before expecting live proof from a restart.

## Failure Handling

- If copy fails with a lock or `user-mapped section open`, RimWorld is still holding the old DLL. Stop or ask the user to close RimWorld, copy again, restart, then re-check `/api/v1/version`.
- If `/api/v1/dev/endpoints` or `/api/v1/version` shows the old shape after rebuild, the running game loaded the old installed DLL. Check hashes and restart RimWorld before debugging code.
- If `localhost:8765` times out, check `Get-Process RimWorldWin64`. If RimWorld is running but no map is loaded, use `/api/v1/version` and `/api/v1/dev/endpoints` for loaded-DLL proof; use `/api/v1/game/state` only for live-colony readiness.
- Do not add `Release-1.5`, `RIMWORLD_1_5`, or compatibility paths. The fork target is RimWorld 1.6 only.
