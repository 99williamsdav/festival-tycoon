# Farm surround A — 4 October 2026

The approved fields-and-lane direction now surrounds the existing farm with continuous ground on every side/corner, a lane continuing from the existing gate, broad muted field patches, low faceted divisions and thirty shared trees in ten sparse clusters. Existing playable terrain, hedge transforms, buildings and placements are unchanged. The open near buffer stays sharp; restrained atmospheric colour mixing begins only beyond 65 m from the farm centre. No global fog, depth-of-field blur, night-cycle change or gameplay expansion is added.

## Source and scope

[Concept approval](../../assets/source/environment/farm-background-concept-a/APPROVAL.md), original handoff, selected A image and original prompts are preserved. Generated layout is not used as authoritative geometry. [FarmSurround.cs](../../game/FarmSurround.cs) builds an independent visual-only node: no collision bodies, navigation regions, input handlers or placement registration. It can be replaced for the user's future surrounding-field expansion, recorded in [B-022](../BACKLOG.md). No tier/unlock framework is implemented.

Ground extent is conservatively derived from the camera's existing maximum zoom (82), pan limit (24), elevation and current viewport aspect ratio, with 64 m safety margin. It rebuilds on viewport resizing. Four ground strips overlap beneath the perimeter and leave a central hole, so they cannot overwrite playable grass. The same small set of meshes/materials is shared through three instance batches (hedges, crowns, trunks), plus ground and two lane meshes. Trees cast shadows; hedges do not add shadow passes. No per-frame scenery update or simulation is required.

## Verification

- Game and test builds succeeded with zero warnings/errors. 28 existing camera, farm, hedge-boundary, placement and navigation tests passed. [Commands/results](../../reports/evidence/farm-surround-a/tests.txt).
- Native 1080p and exported EXE at 720p/1080p each passed 24 views: all four rotations, maximum zoom at each pan corner and minimum zoom at the gate. Ground-plane corner projections remain inside the surround. Exported 2560x720 additionally passed the same 24-view scenery stress check.
- Eight outside placement probes (unlimited Bin type, avoiding tap-limit false positives) were rejected; eight outside downward physics-pick rays missed. Scene traversal found no collision/navigation nodes in scenery. The authoritative simulation hash remained unchanged throughout each fixture.
- Four final native/exported overview angles, selected extreme-pan views and the sharp gate close-up were visually inspected. [24 exported 1080p captures and measurements](../../reports/evidence/farm-surround-a/exported-1080/). [720p](../../reports/evidence/farm-surround-a/exported-720/) and [wide stress](../../reports/evidence/farm-surround-a/exported-wide/).

## Render observations

RTX 3050 Ti laptop GPU, Godot 4.7.2 GL Compatibility. Identical paused preparation scene at size 82, alternating scenery hidden/visible for each orientation, vsync/frame cap disabled only in the opt-in fixture. Each state warms for 30 frames then measures 90. These are whole-app wall-frame timings, not GPU timestamps or a live-festival benchmark.

| Exported 1080p view | Median off/on ms | p95 off/on ms | Draws off/on |
| --- | --- | --- | --- |
| South | 1.707 / 1.637 | 11.961 / 12.445 | 577 / 585 |
| West | 1.709 / 1.516 | 12.607 / 11.975 | 577 / 585 |
| North | 1.324 / 1.548 | 12.264 / 11.941 | 577 / 585 |
| East | 1.301 / 1.534 | 11.955 / 11.888 | 577 / 587 |

Measured overhead is 8–10 draw calls and 46,188–49,188 submitted primitives including shadow passes. Timing deltas are small and noisy (including negative deltas); this does not establish a speedup, exact GPU cost or sustained 60 FPS. Paired raw measurements are preserved for all runs.

## Playtest delivery and caveats

`C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-farm-surround-2026-10-04/FestivalTycoon.exe`, with adjacent `data_Festival.Game_windows_x86_64/` and four screenshots. No ZIP. EXE SHA-256: `7dcd27206fc118e332a93efe9143364ed26f16a384f5a2e1464bc30b0204acbc`. The same executable was launched for the exported checks; copy hash verified.

No manual playthrough is claimed. The existing triangulated entrance apron remains unchanged; the new lane joins beneath it. The 32:9 stress capture reveals pre-existing checklist/HUD overflow, so only countryside coverage is certified there, not wide-screen UI acceptance. Runtime sandbox warnings concern certificate-store access and shader-cache writes; resource rendering and all fixture checks completed successfully. Export had only the existing documented extracted-texture `get_multiple_md5` warnings. Existing eight unrelated character import edits, lead-outfit source and StaffPanel UID remain outside this commit.
