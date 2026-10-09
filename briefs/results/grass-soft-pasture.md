# Soft Pasture grass — 9 October 2026

The user selected A from the [grass concept board](../../assets/source/environment/grass-material-concepts-v1/grass-directions-v1.png). The festival field and adjacent cow pasture now use muted olive colour drifts and fine, irregular grass strokes. This removes the repeating lime triangles and broad mowing bands from the playable field, with smaller, sparse flowers concentrated near the hedges. Detail fades with projected pixel size at farm zoom.

This is a native procedural material interpretation of the approved bitmap concept. The concept is illustrative; it is not an in-game screenshot or a texture pasted onto the mesh. Runtime implementation is in `game/Main.Ground.cs`, with one material hookup in `game/Main.Cows.cs`. The pasture's grazed/worn palette slots are recoloured; its other dressing slots retain the original palette. No grass geometry or additional draw calls are introduced.

## Preserved behavior and scope

The existing wear-map smoothing, flattened/browned/bare-earth thresholds, wetness/mud maps and water precedence remain intact. The pasture material bypasses field simulation maps. Ground simulation, navigation, saved state, placement, cow behavior and pond footprint are unchanged. The original field and pasture GLB blobs match HEAD; their geometry and palette source assets are untouched. Decorative gate apron, hedges and distant countryside are outside this material change.

## Verification

- Game and test builds completed with zero warnings/errors; headless import and Windows release export succeeded.
- The import log contains nine `get_multiple_md5` file-access errors while reimporting character palettes; import continued and the final export completed. These character import files are outside the grass commit. The grass shader rendered successfully in both exported fixtures.
- All 24 ground/navigation tests passed (0 failed, 0 skipped).
- The actual exported EXE produced matched before/after captures for four camera orientations, farm and close zoom, at 1920×1080 and 1280×720.
- The opt-in fixture also shows healthy, flattened, browned, earth, soaked, puddle, mud and sludge bands using synthetic display maps. It restores actual maps and asserts the authoritative simulation hash is unchanged. Both exported runs report `passed: true` and `simulationUnchanged: true`.
- Visual review covered all four 1080p farm directions, a close view, the synthetic bands and the 720p farm view. No manual festival playthrough was performed.
- All 194 delivered EXE/data files match the tested export by SHA-256.

[Native evidence, logs and integration hashes](../../reports/evidence/grass-soft-pasture/) include all captured PNGs and JSON reports. [Before](../../reports/evidence/grass-soft-pasture/1080/0-south-farm-before.png) / [after](../../reports/evidence/grass-soft-pasture/1080/0-south-farm-after.png), [close view](../../reports/evidence/grass-soft-pasture/1080/0-south-close-after.png), [state bands](../../reports/evidence/grass-soft-pasture/1080/synthetic-wear-wet-mud-bands.png).

## Measured rendering comparison

Fixed preparation scene, farm zoom, 30 warm-up frames followed by 90 samples for each material; uncapped whole-app wall-frame measurements:

| Resolution | Material | Median ms | p95 ms | Draw calls |
| --- | --- | ---: | ---: | ---: |
| 1920×1080 | Before | 4.0829 | 21.2771 | 659 |
| 1920×1080 | Soft Pasture | 3.7536 | 20.9085 | 659 |
| 1280×720 | Before | 3.1719 | 20.5660 | 659 |
| 1280×720 | Soft Pasture | 3.3331 | 17.4750 | 659 |

Draw count is unchanged. Small, mixed timing differences are noisy and do not establish a speedup, isolated GPU cost or live-festival FPS guarantee. Ambient cosmetic poses can differ between captures.

## Reproduction and delivery

`game/Main.GrassEvidence.cs` is opt-in via `--capture-grass <output> --grass-before-shader <baseline shader path> --capture-size 1920x1080`. The preserved baseline is `reports/evidence/grass-soft-pasture/before.gdshader`; ordinary startup has no dependency on this file. Fixture output remains separate from game saves.

Windows playtest: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-soft-pasture-2026-10-09/FestivalTycoon.exe`, with adjacent `data_Festival.Game_windows_x86_64` directory. EXE SHA-256: `c243078c2b05455c1463dd8ef63f72d32c98cca5c16f29b806211ea27a8c9cd8`. Run the EXE directly; keep its data directory beside it. This export includes the current shared workspace; the commit is restricted to the grass material, approved concept provenance, fixture and evidence.
