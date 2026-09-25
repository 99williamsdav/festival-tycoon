# R0.03 user-authorised medical playtest revision — 25 September 2026

Status: implemented and builder-verified; independent review and a human exported-game playtest remain pending. This is a follow-up to R0.03, not R0.04.

The water standpipe moved from grid (120,150) beside the stage to (95,123), in open ground at the upper-right of the default view. Its ten owned queue slots are irregular and compact with minimum centre clearance 1.41 m. The approved v3 GLB was not edited: a reversible runtime board/lettering overlay reads **WATER**. One physically arrived drinker receives continuous thirst relief of 16 and heat relief of 4 per simulation tick, leaves at zero thirst, and has `Drinking` intent. Selectable water/first-aid inspectors expose the queue, service, medic, patient and available actions.

The three band members now have versioned `Performer` medical-need profiles in the same hashed/saved snapshot as guests, selected-person thirst and heat bars, water/rest autonomy, distress, horizontal collapse, critical/death progression and physically reached medic treatment. The initial set still starts. Guest 20 retains the original untreated death and timely counterfactual prevention. Save compatibility advanced from medical v6 to v7 because the authoritative schema and service semantics changed; older saves are not silently interpreted as v7.

Visual fixture (pinned Godot 4.7.2, seed `20260922`, booked folk act, steward and safe generator):

| Route | Observed | Frames |
| --- | --- | --- |
| `--capture-medical escalate` | Tick 2,000 distress/three-person queue; horizontal collapse during live set; tick 6,200 `Terminal`, one casualty. | [Upper-right water point](escalate-pick/water-queue.png), [performer bars](escalate-pick/performer-needs.png), [water inspector](escalate-pick/water-selected.png), [collapse](escalate-pick/collapse.png), [first-aid inspector](escalate-pick/first-aid-selected.png), [outcome](escalate-pick/outcome.png) |
| `--capture-medical prevent` | Physical medic travel/treatment; tick 6,200 `Treated`, zero casualties; band remained live. | [Warning](prevent-pick/warning.png), [treatment](prevent-pick/treatment.png), [first-aid inspector](prevent-pick/first-aid-selected.png), [outcome](prevent-pick/outcome.png) |

The screenshots were visually inspected. The standpipe and `WATER` cues are visible in the upper-right field; the collapsed body is horizontal during the rescue window; the performer inspector shows blue thirst and orange heat bars. The fixture calls the game's normal projected-screen `Pick` ray for both facilities and asserts the intended selection; human mouse interaction and readability at every zoom/rotation remain unverified.

Verification:

- `dotnet build FestivalTycoon.sln --no-restore`: passed, zero warnings/errors.
- Direct built-DLL tests filtered to `MedicalIncidentTests|LivePerformanceTests|PreparationTests|EquipmentIncidentTests|FarmSceneTests|IncidentAudioCueCursorTests`: **47/47 passed**, including performer water, set-start, treatment, and death regressions and continuous-drinking/save-restore checks.
- `git diff --check`: no whitespace errors (Git printed only Windows line-ending conversion notices).
- `dotnet build game/Festival.Game.csproj -c ExportRelease --no-restore`: passed, zero warnings/errors.
- Initial revision export `r003-user-revisions-3` completed and passed startup; it was superseded by the review-repaired build below. Its launcher was 127,215,768 bytes, SHA-256 `76677E10C3414BE13F6C280853DC2FE3B0ED2A767AC7F12EAF2E9772B30A7334`. The managed DLLs, not just the launcher, identify an exact export build.
- `Start-Process -Wait -WindowStyle Hidden` of that exact EXE with `--headless --quit-after 60` exited 0. Its fresh Godot log contained `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY`, and `INCIDENT_AUDIO_READY ... mode=cc0-integrated`.

The rejected untracked first-aid v1 source, runtime and review package were not staged or changed. The existing approved first-aid v2 and water v3 assets remain in use. No R0.04 implementation, push, human exported playtest, broad pacing or independent acceptance claim is made here.

## Independent-review repair

Review of commit `031cc18` found three cross-system state defects. A pre-entry performer who drank could be sent directly to the stage without satisfying access/stair invariants; an on-stage performer could be retargeted while `OnStage` and instrument attachment remained true until the next live tick; and Guest 20's water/rest prevention could mark the shared medic response completed while Riley was treating a performer. The repair routes returns through the ordinary access/stair sequence, clears performer stage/attachment state atomically before medical retargets, and preserves a different patient's active response when Guest 20 is relieved. No new save fields or asset changes were needed.

Three new regressions prove a full pre-entry drink and access/stair return across restores, immediate save/restore after on-stage water retarget, and concurrent performer medic response through Guest 20's relief. `dotnet build FestivalTycoon.sln --no-restore` passed with zero warnings/errors. The same focused direct-DLL filter now passed **50/50** tests. `git diff --check` had no whitespace errors. Both pinned-Godot fixtures were rerun after the repair in temporary output: escalation logged `Collapsed`, then `Terminal`/one casualty; prevention logged treatment, then `Treated`/zero casualties. Both facility pick rays still selected the correct inspector.

Final playable artifact: `artifacts/windows/r003-user-revisions-4/FestivalTycoon-R0.03.exe` with its adjacent .NET data directory. `dotnet build game/Festival.Game.csproj -c ExportRelease --no-restore` passed with zero warnings/errors; Godot `--export-release 'Windows Desktop'` exited 0 and ended `[ DONE ] savepack`. The EXE is 127,215,768 bytes, SHA-256 `76677E10C3414BE13F6C280853DC2FE3B0ED2A767AC7F12EAF2E9772B30A7334`. Its final `Festival.Game.dll` SHA-256 is `EF4EB2AFD09BBCDB10437693A95D529F6ECAA4D0732EE7598F1C46782099DF8C`; `Festival.Simulation.dll` is `D2CE8AD6C77153EC196A1068F7C75804994DCB40BB13CB7DB765BC96DB1DB119`. Waited hidden headless startup exited 0 with fresh `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY` and integrated-audio markers. Human exported playtest and independent re-review remain pending.
