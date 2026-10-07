# Pond polish and cosmetic ducks — 7 October 2026

The approved pond now uses the Designer's softer bank, sparse stones/reeds/lily pads and separate drake/brown duck. The old visual, including its baked ducks, is replaced at the existing (24,0,24.5) transform. Original pond GLB/.blend/source remain preserved. The exact 289 blocked cells, navigation, collision and gameplay are unchanged.

[Approved concept](../../assets/source/environment/pond-polish-concepts-v1/HANDOFF.txt), [verified Designer handoff](../../assets/source/environment/pond-polish-asset-v1/HANDOFF.txt) and reproducible source/selected exports are preserved. The handoff's pre-approval concept wording is superseded by the user-authorized static build plus runtime integration. Source/runtime GLBs match the Designer's verified SHA-256 values. [Integration hashes](../../reports/evidence/pond-polish/integration.json).

Two fixed, unsynchronised spline routes stay inside the delivered conservative swimming polygon. Quintic easing gives slow glides, smooth turns and short rests; an occasional whole-body bob is limited to 3 mm. Tiny shared arc meshes follow the ducks and fade with speed/bob. Broad water colour/highlight modulation uses an explicit phase uniform, never wall-clock shader TIME. One private scene-local clock controls every pond effect, freezes on pause/Failed/Finished/hidden/title-menu states and resumes continuously. No simulation actor, gameplay RNG, saved-state field or new interaction is added.

Runtime material corrections keep the delivered palette readable under the farm's stronger warm lighting: bank/details diffuse multiplier 0.55, ducks 0.8 and vertex-tint conversion for the water shader. Source geometry/palette are unchanged. No reflection camera, SSR/refraction, particles, render-pipeline change or night-cycle change. Static/duck geometry totals 2,177 triangles; the two wake instances add 192 triangles before shadow passes.

## Validation

- Game/test builds: zero warnings/errors. 29 focused pond, farm, hedge, placement and navigation tests passed. Twenty minutes sampled at 0.1-second intervals checks complete swept duck radius against shore/lilies, route containment, separation, turns/speed and bob limits. [Commands/results](../../reports/evidence/pond-polish/tests.txt).
- Actual native and exported fixtures pass game pause, Failed, Finished, hidden pond and title menu: clock, duck/wake transforms, wake parameters and water phase freeze together; resume advances exactly the new 0.02-second delta. Cosmetic advance leaves the authoritative simulation hash unchanged.
- All 289 pond cells remain unwalkable/unplaceable; no new pick/collision bodies, unchanged transform. Original pond/nav/concept reference hashes and exact blocked-cell list verified.
- Actual exported EXE captures at 720p and 1080p: all four rotations at close, gameplay size 32 and farm overview size 66. Inspected four-angle closeups, gameplay-scale views and animation frames. Ducks remain naturally small at farm zoom. [1080p evidence](../../reports/evidence/pond-polish/exported-1080/), [720p evidence](../../reports/evidence/pond-polish/exported-720/), [15-second motion clip](../../reports/evidence/pond-polish/pond-motion.mp4). Clip uses 180 actual export frames at 12 fps and real-rate cosmetic time in a staged preparation scene.

## Cost and limits

Godot 4.7.2 GL Compatibility on RTX 3050 Ti laptop GPU. Fixed gameplay-scale view, vsync/frame cap off in the opt-in fixture; full pond hidden versus visible, each 30 warm-up + 90 measured frames. 1080p median 2.064 ms hidden / 1.971 ms visible, p95 13.657 / 14.151 ms. 720p median 1.752 / 1.949 ms, p95 13.529 / 13.777 ms. Full pond/effects add 12 draw calls (324 to 336). These noisy whole-app wall-frame samples do not establish a speedup, exact GPU cost, old-versus-new delta or live-festival FPS guarantee.

No manual playthrough claimed. The reflection treatment is restrained colour/highlight motion, not a full reproduction of the generated concept's painterly reflections. Runtime logs include environmental certificate/shader-cache warnings; all fixture checks completed. The first development capture failed in its preparation-only grid query, was stopped, and the corrected fixture passed; failed captures are excluded from final evidence. Unrelated dirty imports, packages.lock, lead-outfit/rigged backups and other work are excluded.

## Delivery

`C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-pond-polish-2026-10-07/FestivalTycoon.exe` plus adjacent `data_Festival.Game_windows_x86_64/`, four screenshots and motion clip. No ZIP. EXE SHA-256: `5ee9d7e1bc293675fcb659e79e7d15c70689dae25eaebe2fb1afd638db4d5d08`. EXE and all 193 data files verified against the tested export.
