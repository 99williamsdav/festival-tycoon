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
- Fresh isolated Godot `--export-release 'Windows Desktop'` exited 0 and ended `[ DONE ] savepack`. Exact playable artifact: `artifacts/windows/r003-user-revisions-3/FestivalTycoon-R0.03.exe`, 127,215,768 bytes, SHA-256 `76677E10C3414BE13F6C280853DC2FE3B0ED2A767AC7F12EAF2E9772B30A7334`, plus adjacent .NET data directory. Its `Festival.Game.dll` SHA-256 is `C3FB4A0203E06E0C6F9254F3E6FE1D17A95C17C418D604CB254CC866AE199962` and `Festival.Simulation.dll` is `3AB09FF00C72E9AB22823A39CB62909704D5B7BF42361220C385DF6A8336B87A`. The EXE launcher hash matches earlier task-owned exports, but the managed DLL hashes identify the final build. No previous export was overwritten during packaging.
- `Start-Process -Wait -WindowStyle Hidden` of that exact EXE with `--headless --quit-after 60` exited 0. Its fresh Godot log contained `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY`, and `INCIDENT_AUDIO_READY ... mode=cc0-integrated`.

The rejected untracked first-aid v1 source, runtime and review package were not staged or changed. The existing approved first-aid v2 and water v3 assets remain in use. No R0.04 implementation, push, human exported playtest, broad pacing or independent acceptance claim is made here.
