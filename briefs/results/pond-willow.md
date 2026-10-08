# Deep-weeping willow integration — 8 October 2026

The approved [v2 willow](../../assets/source/environment/pond-willow-concepts-v1/APPROVAL.md) is integrated from the Designer's completed [source and handoff](../../assets/source/environment/pond-willow-asset-v1/HANDOFF.txt). One static tree sits on the east bank at pond-local `(4.5,0,0.5)`, yaw PI, identity scale; world root `(28.5,0,25)`. Its long, layered curtains reach slightly into the water. The source palette/geometry are unchanged; one shared diffuse multiplier of 0.6 retains leaf detail under the existing warm farm lighting. Original pond/source, duck GLBs and duck route code remain preserved.

The pond stays at `(24,0,24.5)` with exactly 289 blocked cells. No willow collision/navigation nodes, pick registry entries, placement registration, interaction, simulation actor, wind/weather/leaf animation or reflection pipeline is added. Existing duck/water/wake motion and all pause gates remain intact. Willow geometry is 27,862 triangles in two surfaces sharing one opaque double-sided matte palette material; 143 strands are grouped into one foliage mesh.

## Checks and captures

- Native import, game builds and Windows release export completed. Final focused suite: **29 passed, 0 failed, 0 skipped** (2.905 seconds); pond presentation, build placement, navigation, hedges and farm scene. [Test log](../../reports/evidence/pond-willow/tests-final.log).
- Actual exported EXE checks passed at [1080p](../../reports/evidence/pond-willow/exported-1080/willow-evidence.json) and [720p](../../reports/evidence/pond-willow/exported-720/willow-evidence.json): pause, Failed, Finished, hidden pond and title menu freeze every pond parameter/transform; continuous resume; unchanged simulation hash during cosmetic processing; all 289 cells blocked; two tree surfaces/one shared material; no tree physics/navigation/picks/animation.
- Original duck route, navigation fixture and pond/duck GLB hashes match the recorded baseline. `Main.Pond.cs` only adds the willow construction call. [Baseline hashes](../../reports/evidence/pond-willow/baseline-hashes.json).
- Four orientations at close size 11, player minimum size 18, gameplay size 32 and farm size 66; matched willow-visible/hidden gameplay and farm frames. Inspected all four minimum-zoom views, close water-contact views, full-farm stage sightlines and clip frames. Close size 11 and the size 10 clip are diagnostic views below the player minimum and can crop the crown. Four size 18 screenshots are in the playtest package.
- Six labelled static existing guest-body meshes are **visual stand-ins**, not active simulation people. The marker nearest the trunk is partly hidden from some angles; broader open-field markers remain readable. The stage is clear in the inspected four farm views. No claim of exhaustive crowd visibility or manual playthrough.
- [15-second close duck-motion clip](../../reports/evidence/pond-willow/willow-ducks.mp4): 180 actual exported frames, 12 fps, real-rate cosmetic time, static willow. The capture-only fixture removes the unrelated receipt panel; ordinary player UI is unchanged.

## Geometry and visibility limits

The Designer checked 201 spline parameters per segment against actual source geometry. Minimum clearance beyond a conservative 0.42 m body envelope is 2.042 m for drake and 0.104 m for brown. This is a dense sampled check, not a formal continuous collision proof. Placement/scale and duck paths were retained exactly. Lowest foliage Y=0.055 versus water Y=0.075 gives about 2 cm of water contact. Vertical sampling covers 17.61% of water area, while perspective coverage is larger.

Dense curtains intentionally obscure ducks. Designer head-point samples: drake South 19%; brown South 100%, West 80.1%; both North/East 0%. These are uniform spline-parameter fractions, not playback-time or whole-body visibility percentages. Native captures confirm partial occlusion. The drapes were not shortened to force every view open. Production faceted foliage and existing water highlights do not reproduce the generated concept's painterly reflections.

## Measured overhead

Godot 4.7.2 Compatibility, RTX 3050 Ti laptop GPU; fixed gameplay-size-32 preparation scene with six stand-ins, pond retained, willow hidden versus visible. Each state has 30 warm-up and 90 measured frames with cap/vsync off. **Four added draw calls**: 333→337 at 1080p, 328→332 at 720p. Median wall-frame time hidden/visible: 2.520/2.375 ms at 1080p and 2.213/1.758 ms at 720p; p95 16.681/16.508 ms and 13.628/13.500 ms respectively. Small/noisy differences do not establish a speedup, isolated GPU cost or live-festival FPS guarantee. More foliage geometry than the existing simple trees is an explicit tradeoff.

## Delivery and provenance

`C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-pond-willow-2026-10-08/FestivalTycoon.exe` with adjacent `data_Festival.Game_windows_x86_64/`, four screenshots and clip. No ZIP. EXE SHA-256 `a340391363d9103253ce528dd228bf0ee5b262c81a7ac85e89032990f79bda90`; EXE plus all 193 data files hash-verified against the tested export. [Build/import evidence](../../reports/evidence/pond-willow/).

Export was generated from the current shared workspace, not a clean isolated checkout. Unrelated concurrent simulation/cow work, import changes and other dirty assets are preserved and excluded from the willow commit. Native development logs include existing certificate/shader-cache warnings; the checked fixtures completed. GUI export stdout may be empty, so the completed JSON assertions and actual screenshots are the exported validation evidence. No manual playthrough claimed.
