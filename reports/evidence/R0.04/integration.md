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
