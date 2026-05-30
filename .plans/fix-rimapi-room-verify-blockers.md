# Fix RIMAPI Room Verification Blockers

## Summary

Fix both blockers found in the live `rimapi-room-reads-live-verify` run: deploy the current RIMAPI fork DLL into the active RimWorld mod install, and change Willie so an active Chef freezer request reaches the placement solver instead of being hidden behind generic `kitchen_missing` advice.

Success means `/api/v1/dev/endpoints` shows the current fork routes, `/map/rooms` returns room detail fields, Willie emits `willie_freezer_request_active`, and Build Queue Proposed shows options or a solver no-fit reason.

## Current Evidence

- RimBob Host on `http://localhost:5000` was live from `C:\dev\RimBob` at `5dd05f51`.
- Food emitted a Willie freezer `building_request` with `requested_from = "Willie"`.
- Willie rule replay showed `freezer_request_active` matched with `freezing_building_requests=1`, but suppressed because `kitchen_missing` selected first.
- Live RIMAPI discovery omitted `/api/v1/map/region-at` and `/api/v1/builder/blueprint-group/validate`, while the sibling fork source contains both routes.
- `/api/v1/map/rooms?map_id=0&include_entry_cells=true&include_cells=true&include_contained_buildings=true&include_region=true` returned base room rows but omitted `cells`, `entry_cells`, `region_id`, and `contained_building_ids`.
- The built fork DLL and installed mod DLL differed: `C:\dev\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll` was `994304` bytes with SHA256 `54FE45C4FACA8D15CE561DC8762FD2F4C0BAE5FDC539DF2814E474283702BCEB`; `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll` was `967168` bytes with SHA256 `0FA6573AEA5A6E7D23B7D82AC26F994C6B61AA269A362A21C6DA15484FCA6540`.

## Key Changes

- Build `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj` with `Release-1.6`.
- After RimWorld is stopped, copy the rebuilt `RIMAPI.dll` and `RIMAPI.pdb` from `C:\dev\RIMAPI-for-RimBob\1.6\Assemblies\` to `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\`.
- Verify source and installed DLL hashes match before restarting RimWorld.
- In Willie rules, keep power deficit, low battery, missing materials, and blocked builds ahead of freezer placement.
- Move `freezer_request_active` ahead of generic missing-room rules: `kitchen_missing`, `hospital_missing`, and `storage_room_missing`.
- Keep diagnostics complete: if a kitchen is still missing, it should appear as a suppressed match when the freezer request wins.
- Leave `MinisterOfWillie` solver orchestration intact. Once the selected trace is `freezer_request_active`, the existing solver path should append options or `Placement solver no-fit: ...` to the freezer advice and persist `placement_solver` replay output.
- Update `Docs/design/ministers/construction.md` to record that cross-minister freezer requests preempt generic missing-room advice so solver evidence is visible.
- Mark `rimapi-room-reads-live-verify` done only after the live dashboard shows option cards and the snapshot is captured.

## Test Plan

- Add Willie unit coverage that a freezer request wins over missing kitchen.
- Keep coverage that missing kitchen still wins when no freezer request exists.
- Add coverage that hard blockers still preempt freezer requests.
- Add or adjust minister integration coverage so an inbound freezer flag with missing kitchen runs the placement solver and records either options or `NoAnchors` no-fit replay output.
- Run targeted Willie tests, then `dotnet build .\Src\ApiHost\RimBob.Host.csproj --configuration Debug`.
- After RimWorld restart, verify `/api/v1/dev/endpoints` includes `/api/v1/map/region-at` and `/api/v1/builder/blueprint-group/validate`.
- Verify detailed `/api/v1/map/rooms` includes `cells`, `entry_cells`, `contained_building_ids`, and `region_id` where applicable.
- Trigger Willie and confirm the live freezer advice carries either validated options or a solver no-fit note.
- If options render, run the dashboard snapshot workflow and verify `web/Snapshot/pages/willie/build_queue.html` contains populated Proposed cards.

## Assumptions

- The active mod install path remains `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob`.
- No fork source change is expected because the current fork already contains the missing routes.
- No Apply click is part of this fix unless explicitly requested later; verification is read-only through option rendering and endpoint checks.
