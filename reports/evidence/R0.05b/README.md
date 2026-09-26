# R0.05b staffing evidence — final

26 September 2026. Labelled development demonstration; approved existing bodies, medic vest and steward yoke only. No new art. Agent visual QA, not a human playtest.

- [Capacity without free labour](final/capacity-no-free-labour.png): free future-perk controls already applied, roster remains26; paid £30 medic/steward hire buttons are visible.
- [Paid weekend hires](final/paid-weekend-hires.png):29-person protected roster after optional Avery/Sam and Morgan; same UI offer/payment path.
- [Two medics travelling](final/two-medics-travelling.png): independent Riley/Avery jobs; Avery's name, personal speed/treatment ability and honest route ETA.
- [Named busy buttons](final/named-dispatch-busy-buttons.png): target-owned response disables duplicate actions; named Avery/Sam choices coexist with baseline controls.
- [Physical treatment](final/individual-treatment.png): Riley begins trained360-tick/4.5s treatment only after arrival while Avery still travels.
- [Two stewards travelling](final/two-stewards-travelling.png) and [Sam's route](final/named-steward-route.png): independent routes to real music-cutoff complaints.
- [Manual mid-route save](final/saves/manual-preparation.ftsave): final fixture state, with paid staff profiles and independent assignments. Use matching current content/ruleset; fixture warnings are development-only.

Reproduce from repository root after Debug build:

```powershell
dotnet build game/Festival.Game.csproj --no-restore -v:q
& '.tools/godot/4.7.2/editor/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game --rendering-driver opengl3 -- --capture-r005b-staff 'C:/Projects/festival-tycoon/reports/evidence/R0.05b/final'
```

The capture uses isolated `final/saves`, not normal user save slots. It adds two labelled medical warnings; no person is teleported. Physical dispatch, treatment, payments, incident handling and save coordinators are production logic. Successful marker: `STAFF_CAPTURE_COMPLETE labelled_fixture=true existing_assets=true`.

Normal manual demonstration: in preparation scroll to free staffing foundation controls, grant one role slot, inspect the corresponding named hire tooltip, pay £30, then start the roster. Select an affected person and choose an available named responder; select the responder to inspect role ability, target, route/ETA and treatment remaining. Baseline water/rest/isolation/Riley/Jordan remain available. Human feel/pacing is still pending.
