# R0.04 layout gate — 25 September 2026

Status: **Historical gate, resolved by the later R0.04 layout pass below.** The user requested moving the large barn to the rear field, the approved security post near the entrance, and the first-aid tent farther back, with Jordan and Riley physically based at their facilities and returning after responses. The original trial below was fully reversed under the explicit stop instruction; code then remained at `23d4b9a`, and the restored test project built with zero warnings/errors. The unrelated first-aid v1 files were untouched.

## Tested layout candidate

| Item | Proposed centre/duty position | Result |
|---|---|
| Large barn | Visual centre `(0, -23)` m; blocked navigation rectangle `x=-11..11`, `z=-30..-16` m | This would clear the active front field, but changes the authoritative scenario content hash. |
| Approved security post | Grid `(114,178)`, world `(-6.75,25.25)` m; Jordan's walkable public-front duty cell `(114,182)`, `(-6.75,27.25)` m | Proposed left of the gate road; not rendered or accepted because the coupled trial stopped. |
| First-aid tent | Grid `(116,108)`, world `(-5.75,-9.75)` m; Riley's front entrance `(116,116)`, `(-5.75,-5.75)` m; rest point `(120,116)`, `(-3.75,-5.75)` m | Physical medic travel became too long for an existing treatment route. |

The trial also added physical return-to-base intents after completed medic/security responses. No new graphics, steward yoke/cue, or hearing mechanics were introduced. None of these trial edits remain in the working tree.

## Exact blockers

- `dotnet build tests/Festival.Tests/Festival.Tests.csproj --no-restore -warnaserror` passed with zero warnings/errors on the trial. The focused `DisorderIncidentTests|MedicalIncidentTests|FarmSceneTests` run failed `DistressedPerformerCanReceivePhysicalMedicTreatment`: expected `MedicalStage.Treated`, actual `Critical`. This is an unsafe medical-timing regression for a real physical response, not merely a screenshot difference. Two disorder fixtures also diverged under changed travel/geometry (`SeparatedConfrontationCannotInjureARemoteOpponent` and `FailedCalmingThenGuestInjuryCannotRestartStaleSecurityFight`); the run was stopped rather than spending an unbounded loop retuning fixtures.
- The current R0.04 save slots have content hash `0b7dfbade3fd86317ca3b85ae162e4cf126cf16ec23c1dd211d6a435b672ccd7`, ruleset `r0-disorder-v10`. The save loader rejects a changed content hash unless an explicit migration is implemented. Moving the authoritative barn and navigation obstacle changes that hash. The inspected current R0.04 slots were pre-start, but a safe migration must also define what happens to any active older route crossing the new rear-barn footprint; no silent hash reuse, teleport, or overwrite is acceptable.

## Decision needed

Choose an exact farther-back first-aid area that still passes the existing physical-treatment deadline, **or** authorize a specific medical-response timing change and its safety tests. Separately, approve an explicit old-R0.04-layout save migration policy for preparing and active saves, including a no-teleport refusal if a saved actor occupies a newly blocked cell. After those choices, retest the barn/post/tent placements and all gate, stage, water, medical and security paths together; capture beginning/active default-angle views before committing. No R0.05 work should begin from this gate.

## Read-only first-aid placement sweep (25 September 2026)

No tracked implementation, save, or timing value was changed for this sweep. A key source finding narrows the next test: `MedicalTentCell` positions the rendered tent and its blocked 7×7-cell footprint, but `MedicalMedicCell` is currently **not used** by preparation or dispatch. Riley initially travels to generic `PreparedPlace(index)`; a physical tent base/return therefore requires an explicit code change and cannot be proved merely by moving the tent marker.

The following centres are all behind the current `(-4.25, 22.25)` m first-aid point, clear of the proposed rear-barn block (`x=-11..11, z=-30..-16`), well away from the water tap `(-16.25, -2.25)` m and security post `(-6.75, 25.25)` m. Each proposed duty cell is just south of the tent footprint so Riley can visibly stand outside the entrance. They are **candidates, not accepted placements**:

| Rank | Tent centre (grid; world m) | Proposed Riley duty (grid; world m) | Read-only assessment |
|---|---|---|---|
| 1 | `(116, 144)`; `(-5.75, 8.25)` | `(116, 140)`; `(-5.75, 6.25)` | Closest to the stage/performer route of these candidates; a prudent first physical-treatment trial with unchanged deadlines. Tent is behind the present front/gate position but still beside the stage. |
| 2 | `(116, 138)`; `(-5.75, 5.25)` | `(116, 134)`; `(-5.75, 3.25)` | More visibly back from the gate; about 3 m farther from the stage than rank 1. Treatment margin must be measured. |
| 3 | `(116, 132)`; `(-5.75, 2.25)` | `(116, 128)`; `(-5.75, 0.25)` | Furthest back of this safe-footprint set; highest response-timing risk, so reject if the focused performer fixture or injured-security route misses its deadline. |

The current medical speed is 30 mm/tick before the identity speed factor (0.85–1.15), with 480 ticks of treatment after arrival. A straight-line lower bound from candidate 1 duty to the stage centre `(-16, 11)` m is roughly 11.3 m (about 330–443 movement ticks at those factors); candidate 2 is about 12.8 m and candidate 3 about 14.9 m. These are **not pathfinding or crowded-route results** and cannot establish fixture pass/fail. The trial at duty `(-5.75, -5.75)` m failed the performer treatment fixture, so it should not be reused as evidence that the deadline is safe.

The default medical/disorder camera uses an orthographic size 32, focus `(-16, 0, 11)` m, and a 45° yaw. These candidates should project to the right/rear of the stage instead of the entrance foreground, but visual clearance, label overlap and pickability are unverified until a captured beginning/active default-angle view. The next bounded implementation check should try rank 1 first with a real Riley arrival/return route, run `DistressedPerformerCanReceivePhysicalMedicTreatment` and the injured-security route under unchanged timing, and only then consider ranks 2–3. Save migration remains a separate gate.

## Resolution — authorized development save reset and rank-1 placement

The user authorized discarding incompatible development saves rather than migrating them. No old slots were deleted: R0.04 now writes to `user://saves/r0.04-layout-v11`, and both content and ruleset compatibility values changed. An old-layout save explicitly fails loading with `Content hash mismatch`; the test verifies the file's bytes remain unchanged. Other capture/campaign modes retain the legacy save directory. This is a development-only reset policy, not a migration.

Rank 1 was implemented: tent `(116,144)`, Riley duty `(116,140)`, rest `(120,140)`; security post `(114,178)`, Jordan duty `(114,182)`; large barn `(0,-23)` m with matching rear-field navigation obstacle. Both workers physically arrive at their bases and walk back after completed responses. No medical deadline or walking speed changed.

The first rank-1 performer test failed, but a tick trace showed **treatment began at tick 1337 after dispatch at 990**, then the stage schedule pulled the patient away at tick 1760, interrupting treatment before its existing 480-tick completion. The repair gives an awaiting/collapsed performer medical navigation ownership through treatment; the same fixture now passes without altering response timing. A separate remote-fight fixture was made layout-independent by displacing its actor relative to the initiator and stopping its prior route from snapping it back. No disorder eligibility or injury rule changed.

Checks: Debug test and game builds, plus ExportRelease game build, passed with zero warnings/errors. The focused coupled `DisorderIncidentTests|MedicalIncidentTests|FarmSceneTests` run first exposed the fixture issue; after repair, the wider Disorder/Medical/MedicalCue/LivePerformance/Equipment/Preparation/FarmScene/SaveRoundTrip run passed **84/84** in 47s 303ms. Riley's and Jordan's return-to-base tests pass across save/restore, as do injured-security treatment, queue/gate/stage routes and non-destructive old-save rejection.

Rendered beginning/active South-angle 70-unit whole-site captures are [`beginning-south.png`](layout/beginning-south.png) and [`active-south.png`](layout/active-south.png). Both were visually inspected: rear barn, gate-side post and set-back first aid are distinct and unobscured; the active capture logged `R004_LAYOUT_CAPTURE tick=2400 medic=GridCell { X = 116, Z = 140 } security=GridCell { X = 114, Z = 182 } orientation=South zoom=70`. This is an agent-rendered capture, not a human exported playtest.

Fresh isolated Windows playtest export: `artifacts/windows/r004-layout-v11/FestivalTycoon-R0.04.exe` (127,274,840 bytes). Waited headless startup exited 0 and printed `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY` and `INCIDENT_AUDIO_READY`, with no `ERROR:` lines in the filtered output. Launcher SHA-256 `19E3A9C7C2F834F1C19A86931CF2B4F89FF3912C123D1D106037A199C923F679`; companion Game DLL `FE816AD41CD185D2F82AB0797FA6FB00E2E06F6CCE7A84D6578654C0C3868D9C`, Simulation DLL `88D1ACAAC93B5E437C231340907F95E7760548495C676454896EBCEDBDF2137A`. Human exported playtest/final acceptance remain pending; the unapproved steward yoke was not integrated and R0.05 was not started.
