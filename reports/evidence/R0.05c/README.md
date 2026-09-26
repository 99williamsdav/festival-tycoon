# R0.05c — playtest correction evidence

26 September 2026. Agent-rendered labelled fixtures, not human feel/pacing acceptance. Both batches implemented with isolated export/startup and independent final source/render/evidence gates passed. Full266/267 tests, solely unchanged parked S0.07 diagonal proof failure.

## Batch1 capture

- [Centred selected tap](batch1/centred-selected-normal.png): actual world ray pick, model-centred highlight,48px approved NORMAL icon and preparation-only per-tap Move.
- [Rotated move preview](batch1/rotated-move-preview.png): compact footprint/model and single service-access marker, not a reserved20-person tail. Quarter-turn interaction orientation saves with placement.
- [Extra-tap context](batch1/extra-tap-context-move.png): independently selected extra with its own Move.
- [Organic physical line](batch1/organic-physical-line.png): three guests in the extra tap's real physical-arrival FIFO, with an active drinker. A fourth guest independently drinks at the original tap. The approved narrow model is centred and front interaction is within1m.
- [Boosted tower](batch1/flow-boosted-tower.png), [Council+tower normal](batch1/flow-normal-council-plus-tower.png), [Council low](batch1/flow-low-council.png): exact approved48px trio with text naming both actual modifiers and per-person rates.
- [Manual live save](batch1/saves/manual-water-playtest.ftsave): organic line, paid contracts and saved orientation. Fixture warnings/needs are development evidence, not normal user slots.

Reproduce from the repository root:

```powershell
dotnet build game/Festival.Game.csproj --no-restore -v:q
& '.tools/godot/4.7.2/editor/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game --rendering-driver opengl3 -- --capture-r005c-water 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/batch1'
```

Successful capture requires `WATER_PLAYTEST_CAPTURE_COMPLETE labelled_fixture=true flow_states=3 approved_icons=48px`, startup markers and no runtime errors, not just exit0. The fixture sets eight guests' thirst to10000 to lengthen refills; no positions are teleported. Travel, physical admission, service, assignments, payment and save coordinators use production logic. Three flow panels reset an isolated prepared baseline; normal user saves remain untouched. Source/runtime approval is [recorded here](../../../art/reviews/R0.05a/tap-flow-icons-v1/APPROVAL.md).

Manual route: select any tap during preparation → Move this tap → comma/period rotate → green grass/access preview → left-click commit. Right-click/Esc cancels without changing state. Add up to two extras, inspect actual Council/tower flow, start the roster, then inspect physically arriving queues and personal needs. Approaching reserves no place; abandoning a queue forfeits it. Human placement and queue feel remains pending.

## Batch2 capture

- [Named abilities and approach](batch2/named-abilities-and-approach.png), [target status](batch2/named-target-physical-help-status.png), [staff help controls](batch2/named-help-no-remote-controls.png): two independent physical requests, clear personal abilities, named role-specific choices; no remote attendee guidance/removal buttons.
- [Arrival before guidance](batch2/physical-arrival-before-guidance.png): a physical medic reached Guest20 before rest guidance; Sam's independent water approach continues.
- [Actual complaint escort request](batch2/escort-request-actual-complaint.png), [physical escort](batch2/physical-escort-in-progress.png), [individual gate completion](batch2/individual-gate-completion-festival-continues.png): actual power-cut/music complaint, named steward approach, bounded shared waypoints, then one departure at the gate while festival stays Running. Ordinary Restore Music is used after requesting the escort so unrelated people do not escalate; no teleport or safety immunity.
- [Actual collapse](batch2/actual-collapse-visible-selectable.png), [medic approach](batch2/collapsed-physical-medic-approach.png), [physical treatment](batch2/collapsed-physical-treatment.png), [completed counter](batch2/collapsed-counter-completed.png): fresh ordinary baseline; visible/selectable prone guest, continuing death clock, physical dispatch/arrival/treatment and recovery before death.
- [Actual mid-escort save](batch2/saves/manual-mid-escort.ftsave): saved and loaded with exact authoritative hash. [Engine log](batch2/capture.log) records both physical completion assertions.

```powershell
dotnet build game/Festival.Game.csproj --no-restore -v:q
& '.tools/godot/4.7.2/editor/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game --rendering-driver opengl3 --log-file 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/batch2/capture.log' -- --capture-r005c-staff 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/batch2'
```

Required markers: `guidance_completed=2 escort_gate=True festival_running=True fixture_remote=False people=28`, `STAFF_INTERVENTION_COLLAPSE_CAPTURE treated=True fixture_remote=False people=28`, `STAFF_INTERVENTION_CAPTURE_COMPLETE`, normal launch/scene/audio and no engine errors. This capture advances actual simulation to bounded milestones and pauses for screenshots; it is not FPS, continuous real-time or human feel evidence. No needs, health clocks or positions are injected. Historical water/incident screenshot fixtures now explicitly opt into saved development-only remote commands; this physical capture refuses that gate.

Manual route: start a staffed weekend, select a guest, scroll to Physical Staff Help, choose an on-duty named steward/medic and request its eligible action. Inspect the worker's route/target; nothing happens to the guest before arrival. Request Escort Out only for an affected ambulatory person; observe shared physical waypoints to the gate. Collapsed/fighting/injured people require ordinary physical aid/resolution first. Select a collapsed guest and dispatch a medic; the prone visual persists during approach and treatment. Human staff/queue feel remains pending.

[Fresh native export startup log](batch2/export-startup.log). Export/artifact identity and test timings are recorded in [the result](../../../briefs/results/R0.05c.md).

## Historical-capture compatibility reruns

[Legacy water rerun log](legacy-water-rerun/capture.log) and [manual live save](legacy-water-rerun/saves/manual-water-playtest.ftsave) retain the labelled thirst/route fixture under its explicit saved development gate. [Legacy staff rerun log](legacy-staff-rerun/capture.log) and [manual staff save](legacy-staff-rerun/saves/manual-preparation.ftsave) retain two concurrent medic and steward responses after real on-site rest guidance. Both exit0/complete markers; original batch1/R0.05b evidence untouched. These reruns are tool compatibility, not new human acceptance. Named generated fixture autosaves/backups were removed, not normal user slots; they are regenerable.

## Producer follow-up — normal UI has no water closure

[Live help controls](water-ui-cleanup/named-help-no-remote-controls.png) and [actual complaint](water-ui-cleanup/escort-request-actual-complaint.png) show no Close Water/Reopen Water, with music reset and staff-mediated help retained. [Capture log](water-ui-cleanup/capture.log) asserts `closure=False reopen=False` and completes the physical guidance/escort/collapse demonstration. [Export log](water-ui-cleanup/export.log) and [fresh native startup](water-ui-cleanup/export-startup.log) retain exit0/marker verification. This presentation-only follow-up leaves simulation/diagnostic support unchanged; no new full-suite result or human pacing claim.

## Audience correction — final render

[Natural forty-person audience](audience/natural-forty-person-audience.png) and [low-interest comfort/pace inspector](audience/natural-audience-comfort-inspector.png) use ordinary admission, not injected positions. [Labelled rear density before](audience/labelled-rear-density-before.png), [physical12s adjustment](audience/physical-density-after-12s.png) and [physical72s redistribution](audience/physical-density-after-72s.png) use one explicit initial rear-position fixture, then ordinary shared physical walking only. [Capture log](audience/capture.log) records actual physical front counts/positions and exact continuation assertion; [mid-density save](audience/saves/manual-audience-density.ftsave) retains saved places/clocks.

Reproduce after Debug build:

```powershell
& '.tools/godot/4.7.2/editor/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game --rendering-driver opengl3 --log-file 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/audience/capture.log' -- --capture-r005c-audience 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/audience'
```

Require startup/scene/audio, `AUDIENCE_CAPTURE_COMPLETE physical_redistribution=True replay=True no_teleports_after_labelled_setup=True existing_assets=True`, and no engine errors; exit0 alone is insufficient. Final rear sequence physically moves front0→0→11→22 at0/12/42/72seconds, with40watching at each checkpoint and exact saved continuation. Normal generator load-shed is included before the long fixture, not a disabled hazard. No whole-weekend, real-time/FPS, larger-capacity or human feel claim.

[Cross-priority physical staff rerun](audience-staff-regression/capture.log) preserves real guidance/escort/collapse treatment with normal fixture flag false; [manual mid-escort save](audience-staff-regression/saves/manual-mid-escort.ftsave). [Audience export log](audience/export.log) and [fresh native startup](audience/export-startup.log) identify the isolated audience build. Final test counts, timings, review gate and artifact hashes are recorded in [the result](../../../briefs/results/R0.05c.md).

## Small audience lateral/backstep refinements

[Natural dispersed crowd](audience-refinement/natural-refined-forty-person-audience.png), [labelled extreme side before](audience-refinement/labelled-extreme-lateral-before.png), [physical recenter after](audience-refinement/physical-extreme-lateral-after.png): selected person physically moves103165→103161. [Labelled density before](audience-refinement/labelled-density-retreat-before.png), [physical retreat facing stage](audience-refinement/physical-density-retreat-stage-facing.png), [ordinary route facing reset](audience-refinement/ordinary-route-facing-reset.png): short actual retreat retains stage-facing; subsequent labelled `medical.rest` navigation route resumes travel-facing. Each separate initial geometry is a development fixture; subsequent displacement uses shared walking. Paused capture settles cosmetic yaw using real before/after positions, not injected movement.

[Capture log](audience-refinement/capture.log), [16/16 focused tests](audience/audience-final-heading-tests.log), [export log](audience-refinement/export.log) and [fresh native startup](audience-refinement/export-startup.log). No new broad-suite/FPS/capacity/human-feel claim.

```powershell
& '.tools/godot/4.7.2/editor/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game --rendering-driver opengl3 --log-file 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/audience-refinement/capture.log' -- --capture-r005c-audience-refinement 'C:/Projects/festival-tycoon/reports/evidence/R0.05c/audience-refinement'
```

Require normal launch/scene/audio, `AUDIENCE_REFINEMENT_CAPTURE_COMPLETE lateral_physical=True retreat_stage_facing=True ordinary_route_reset=True labelled_initial_geometry=True existing_assets=True`, actual facing dot assertions and no errors; exit0 alone is insufficient.
