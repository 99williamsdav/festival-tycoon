# R0.04 integration checkpoint — 25 September 2026

Status: implemented and builder-verified; independent review and human exported
playtest are pending. The approved pressure-driven music/water disorder chain is
in the working tree; this is not an independent acceptance claim.

Implemented so far: persistent per-person temperament, queue tolerance, grievance,
pressure and stage; complaint → agitation → argument → eligible abstract fight;
spontaneous diffusion; safe explicit ≤80% music reset after isolation; physical
water-line grievance and closure/egress; security dispatch with physical travel,
distinct calm/confront skills, possible security injury; shared medic treatment and
attributed terminal outcome. The existing HUD and selected-person inspector expose
pressure, stage, cause and response; no new unapproved graphics were integrated.
R0.03's rejected first-aid v1 remains untouched.

Checks completed:

- `dotnet build tests/Festival.Tests/Festival.Tests.csproj --no-restore -warnaserror`: pass, zero warnings/errors.
- `dotnet tests/Festival.Tests/bin/Debug/net8.0/Festival.Tests.dll --filter "FullyQualifiedName~DisorderIncidentTests" --progress off --show-test-results all`: 8/8 passed (5s 226ms).
- Same runner, combined MedicalIncident/MedicalCuePlanner/LivePerformance/EquipmentIncident/Preparation/Disorder filter: 64/64 passed (32s 752ms).
- Godot editor runtime rendered both music and water complaint/prevention screenshots under `music/` and `water/`; output logs reported causal complaint followed by safe reset or closure. All four final screenshots were visually inspected: selected-person pressure/stage/cause, live music after reset, and empty queue/closed water after closure are readable. The post-intervention complaint pressure decays rather than being erased instantly, but grievance/eligibility are removed.
- `dotnet build game/Festival.Game.csproj -c ExportRelease --no-restore -warnaserror`: pass, zero warnings/errors. Godot 4.7.2 `--headless --export-release 'Windows Desktop'` created isolated `artifacts/windows/r004-disorder-1/FestivalTycoon-R0.04.exe` (127,264,312 bytes). A waited `--headless --verbose --quit-after 120` run exited 0; the fresh Godot application log contained `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY`, and `INCIDENT_AUDIO_READY` markers. The launcher SHA-256 is `0D2C88850430750A4F78D0FD218C8880AAB5D81DA19643029D8ADE7B7A9757C4`; adjacent `Festival.Game.dll` is `D8A7BF6A4543F4BA6BEEBF0B03CC32CCC5E887816E147142921219C859F236A2`; `Festival.Simulation.dll` is `09635B2E56C24A60B8BF950AAAB6987A33CED62FDC8E1CBEB0A3D9B3C22D1411`.
- Six paired seeds (51–56) with water closed and music safely reset had zero complaints/fights/casualties; the same seeds with the music cutoff unresolved produced complaints and spontaneous argument diffusion. The low-skill security injury fixture used a bounded natural seed sweep and is explicitly a development fixture, not an authored mandatory incident.

The first low-skill injury fixture failed after a pressure-rate tuning change because
that seed naturally followed another terminal branch; this was a fixture assumption,
not a forced gameplay incident. A bounded natural-seed fixture now finds a security
injury and tests physical treatment and an untreated labelled terminal counterfactual.
The first countered-state sweep attempted a safe reset before the live-set stage had
observed isolation; advancing eight ticks fixes ordering. All eight R0.04 tests now
pass. No current blocker.

Remaining: independent review and human exported playtest. The approved exact
security post is designer-owned and not integrated in this implementation; role
and incident cues use the approved existing text/state presentation only. Wider
human pacing and hearing economy are out of this brief.

## Independent-review repair — 25 September 2026

Astra review of `2449600` found four actionable defects. This bounded repair:

1. Releases water-approach seekers as well as queue/overflow members on closure, and guards physical admission while closed. A new test closes water while a person is still walking to the line, restores the save, advances 320 ticks and confirms there is no late admission.
2. Removes remote fight suppression while security is merely travelling. A bounded seeded test restores the argument/dispatch boundary and proves a confrontation can still begin before arrival; security reports that it was too late.
3. Reserves both abstract-confrontation participants, blocks egress/medical removal during the confrontation, and rechecks admission, reservation and physical separation before any injury. New tests cover shared-opponent ownership, blocked egress, exactly one injury from a coupled pair, and cancellation without remote injury after fixture-only displacement.
4. Persists immutable incident origin (initiator, opponent, grievance, pressure, argument/fight ticks) through injury and death. A guest-opponent terminal test confirms attribution comes from the initiator rather than the injured victim. Save/hash includes this record, and restore validates it.

The paired six-seed counterfactual now compares fights as well as complaints: safely countered music/water has zero confrontations in all six seeds; unresolved music has confrontations and spontaneous diffusion. The latest focused cross-brief run passed **69/69** in 40s 380ms. Debug test build and ExportRelease game build passed with zero warnings/errors. Fresh isolated export: `artifacts/windows/r004-disorder-review-2/FestivalTycoon-R0.04.exe` (127,264,312 bytes). A waited hidden `--headless --quit-after 120` run exited 0; the log timestamp advanced and its fresh log contained application, farm-scene and integrated-audio markers with no `ERROR:` lines. Launcher SHA-256 `0D2C88850430750A4F78D0FD218C8880AAB5D81DA19643029D8ADE7B7A9757C4`; adjacent Game DLL `839D8E7162EA2E98DBB0BEF27FA4918975CF77C57090F701BD87B5CEC20577DB`; Simulation DLL `B0E34D0C2BEBA91377C7810054A899E7D529920D2EF00BB4319382B1368639C7`.

The fixture-only displacement manipulates a navigation coordinate to test the resolver's distance guard; it is not a player action or evidence of natural travel. Human exported playtest and further independent re-review remain pending.

## Stale security-ownership repair — 25 September 2026

Re-review found that a failed calming response could retain `Confronting` ownership after its target entered and lost a separate guest fight. On a later social tick, that stale response could start a security fight over the already collapsed target, obscuring the medical deadline. The response now stands down and clears its target when another fight owns the person, or hands an injured/collapsed person to medical response. A security confrontation can begin only for an admitted, ambulatory `Argument` target still owned by that security response. No general fight or medical timer was changed.

The exact-sequence regression uses real failed calming, a labelled fixture guest confrontation, a guest-victim injury, exact save/reload, and continuation through the original untreated deadline. It verifies no later security fight overwrites the injury. The final focused cross-brief suite passed **70/70** in 57s 323ms. Debug test and ExportRelease game builds passed without warnings/errors. Fresh isolated export `artifacts/windows/r004-disorder-review-3/FestivalTycoon-R0.04.exe` (127,264,312 bytes) passed a waited hidden headless startup: exit 0, freshly advanced Godot log, application/scene/audio markers, no `ERROR:` line. Launcher SHA-256 `0D2C88850430750A4F78D0FD218C8880AAB5D81DA19643029D8ADE7B7A9757C4`; adjacent Game DLL `6458E332CA5BB73D5DE5D99BDBEF43698BFD9795080FDC60F571C096B040C16D`; Simulation DLL `4190515B7D8D4939E5343B27C5DBA319F7246F48A8915038604A1397180A7DB9`. Human exported playtest and independent acceptance remain pending.

## Approved security-post v1 visual integration — 25 September 2026

This is the final R0.04 visual-only integration checkpoint after the logic re-review. The exact approved `assets/runtime/environment/lwf_security_post_v1.glb` was copied into Godot's runtime environment directory; both files have SHA-256 `A92B5B4166F25A96CECA4E62BCFE023B44EBDD6FE22A525CE33603B3088AEC96`. Godot imported its extracted palette and import metadata. No steward role cue or new incident art was added.

The open-sided post sits at grid (116,160), world (-5.75,16.25) m, facing its approved +Z public approach. Jordan's live-edition duty destination is the walkable cell (116,164), world (-5.75,18.25) m, in front of it. The model and pick-only collider do not alter the traversal grid; the post is left of the gate road and apart from the first-aid tent. A dedicated test proves Jordan physically reaches the base and a protected guest still walks to the gate; existing physical dispatch, water and medical tests also pass. Selecting the post by the normal pick ray shows Jordan's live physical position, response owner/target and injury status. Its `SELECT JORDAN • SECURITY` action selects the actual worker for the existing medical inspector; person-targeted dispatch/egress remain on the affected person's inspector. No response mechanics or skill values were changed.

Game-angle, default 32-unit zoom evidence: [`post-default.png`](security-post/post-default.png), [`post-selected.png`](security-post/post-selected.png), and [`worker-selected.png`](security-post/worker-selected.png). The capture log reported `SECURITY_POST_CAPTURE security=28 base=GridCell { X = 116, Z = 164 } tick=553`, `SECURITY_POST_SELECTED`, and `workerSelected=True zoom=32 orientation=South`. All three captures were visually inspected: the post is legible, Jordan stands in front, the road/gate approach remains visually clear, and selection/worker action render in the inspector. This is agent-rendered QA, not a human exported playtest.

The final focused cross-brief suite passed **71/71** (48s 774ms), including the new duty-position/egress regression. Debug and ExportRelease builds passed without warnings/errors. Fresh isolated Windows playtest export: `artifacts/windows/r004-security-post-1/FestivalTycoon-R0.04.exe` (127,274,840 bytes); launcher SHA-256 `19E3A9C7C2F834F1C19A86931CF2B4F89FF3912C123D1D106037A199C923F679`, Game DLL `D525FF55487D721FC8F46E7AF900E63EE3EECE6EC6DCFFA6F9CB65322DFA30B7`, Simulation DLL `FB398FEB3250D3F38A8C845E02AA89BE50193A3A37ACA8DA52AAD3D98473C283`. Waited hidden startup exited 0, advanced its Godot log timestamp, and printed application, farm-scene and audio-ready markers with no `ERROR:` line. Human exported playtest and final visual review remain pending; no R0.05 work.
