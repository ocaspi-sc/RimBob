# RIMAPI Fork Migration Plan

## Summary

Do not use a submodule. Clone the fork as a sibling repo at
`C:\dev\RIMAPI-for-RimBob`, keeping RimBob and the RIMAPI mod as separate
repositories. RimBob continues to integrate over HTTP at
`http://localhost:8765/`.

## Key Changes

- Clone `https://github.com/ocaspi-sc/RIMAPI-for-RimBob` into
  `C:\dev\RIMAPI-for-RimBob`.
- In the RIMAPI clone, update mod metadata:
  - name: `RIMAPI for RimBob`
  - packageId: `Ocaspi.RIMAPI.RimBob`
  - keep Harmony dependency and 1.5/1.6 support.
- Build RIMAPI:
  - project: `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj`
  - configuration: `Release-1.6` for RimWorld 1.6, optionally `Release-1.5`
    to keep the 1.5 assembly current
  - pass `RimWorldPath=D:\Games\SteamLibrary\steamapps\common\RimWorld`
  - on this machine, VS2019 MSBuild cannot load SDK 9; use `dotnet build`
    unless a newer Visual Studio/MSBuild is installed.
- Install the built local mod to:
  - `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob`
- Update RimWorld active mods:
  - replace `redeyedev.rimapi` with `ocaspi.rimapi.rimbob`
  - keep `brrainz.harmony` active before it.
- Update RimBob docs/runtime notes:
  - `Docs/design/RimAPI.md` should name `C:\dev\RIMAPI-for-RimBob` /
    `ocaspi-sc/RIMAPI-for-RimBob` as the active fork.
  - `.plans/mark-harvest-unforbid.md` should point endpoint work at the
    sibling fork repo.
  - leave `unforbid` and plant-growth endpoint implementation todos open until
    actually implemented.

## Test Plan

- Verify clone and fork remote:
  - `git -C C:\dev\RIMAPI-for-RimBob remote -v`
  - `git -C C:\dev\RIMAPI-for-RimBob status --short --branch`
- Verify build output:
  - `1.6\Assemblies\RIMAPI.dll` exists with a fresh timestamp.
  - `1.5\Assemblies\RIMAPI.dll` exists if the optional 1.5 build was run.
- Verify RimWorld config:
  - active mods include `brrainz.harmony`, `ludeon.rimworld`,
    `ocaspi.rimapi.rimbob`
  - active mods do not include `redeyedev.rimapi`.
- Launch RimWorld from
  `D:\Games\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe`.
- After loading a colony, verify:
  - `GET http://localhost:8765/api/v1/game/state`
  - `GET http://localhost:8765/api/v1/dev/endpoints`
- Start RimBob with `C:\dev\RimBob\run-rimbob.ps1` and verify Host reports
  RIMAPI reachable.

## Assumptions

- No submodule, no vendored source, no RimBob pointer to the fork commit.
- RIMAPI fork commits live in `C:\dev\RIMAPI-for-RimBob`; RimBob changes are
  only docs/config expectations.
- This slice migrates runtime/docs to the fork, but does not implement new
  RIMAPI endpoints.
