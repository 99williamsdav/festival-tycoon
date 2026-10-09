# Sculpted Crown A — oak and old apple

The user approved **A — Sculpted Crown** with “A 100%” after reviewing four abstract tree concepts. The mature oak and old apple now use dense connected canopies with overlapping shallow bulges and broad matte facets. There are no individual leaves. Upper branches are concealed; nine restrained faceted apples remain visible on the smaller tree.

[Selected concept, source and provenance](../../assets/source/environment/tree-sculpted-crown-v2/HANDOFF.md). The generated board is illustrative: native models preserve the current game scale and footprint rather than reproducing its exaggerated broad trunk. Blender source, deterministic build script, palette, runtime exports, verification script/results and four imported-GLB review renders are retained. Native captures are the evidence of actual implementation.

## Integration and preserved rules

`game/Main.cs` switches only the oak/old apple asset paths to v2 at their existing positions: (-32, 0, 9) and (-29.2, 0, -6.6). Original v1 models/source remain available. Both new assets retain separate `Trunk` and `Crown` mesh nodes, and the existing .2-strength Crown breeze hookup. Low trunk/root radii remain within .45m and .18m. The canopy envelope matches original bounds; some apple fruit extends at most approximately .12m beyond the previous whole-asset box.

Navigation/building blocked cells, placement, gameplay shade rules and simulation code are unchanged. No collision, navigation region, animation player or skeleton nodes are added. The field maple, willow, hedges and grass are unchanged by this commission.

## Verification and limits

- Game build succeeded with zero warnings/errors; final Windows release export completed.
- Separate imported-GLB checks passed: node/material counts, bounds, low-trunk radii and triangle counts; seam-welded topology confirms one connected foliage skin per tree (plus nine apple fruit components).
- Actual final EXE fixtures at 1920×1080 and 1280×720 produced four camera directions, each with matched old/new farm, oak-close and apple-close views.
- Both fixtures verify the authoritative simulation hash is unchanged during presentation checks. Crown sway is observed; both Crown transforms and paused simulation hash remain exact over 20 paused frames.
- Visual inspection covers farm and close views, including foliage outline, branch concealment, fruit and trunk geometry. The initial too-round/too-bright candidate and pinched trunk were corrected before final delivery.
- No manual festival playthrough, new simulation test suite or full navigation sweep is claimed. This is a bounded cosmetic asset change with unchanged navigation source. Fixture baseline trees are static; the new crowns use their ordinary ambient sway, so poses differ slightly.

[Native evidence and build/export logs](../../reports/evidence/tree-sculpted-crown/). `--capture-trees <output> --capture-size 1920x1080` runs the opt-in preparation fixture. Ordinary startup does not create the before/after comparison objects.

## Cost and delivery

Old oak/apple combined: 704 triangles. New combined: 3,784 triangles (oak 1,522; apple 2,262). Two mesh nodes and one palette material per mesh are retained. Fixture JSON records fixed-view draw calls and uncapped whole-app wall-frame median/p95 measurements, 30 warm-up plus 90 sampled frames per state. Timings are noisy and do not establish isolated GPU cost or live-festival FPS guarantees.

| Resolution | Trees | Median ms | p95 ms | Draw calls |
| --- | --- | ---: | ---: | ---: |
| 1920×1080 | Original | 5.4623 | 30.5382 | 659 |
| 1920×1080 | Sculpted A | 5.4259 | 31.5583 | 659 |
| 1280×720 | Original | 3.0940 | 18.8524 | 659 |
| 1280×720 | Sculpted A | 2.9716 | 18.6706 | 659 |

Windows playtest is delivered as `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-sculpted-trees/FestivalTycoon.exe` with its adjacent Godot .NET data directory, no ZIP. Hash verification for copied files and source/runtime asset agreement is recorded in `reports/evidence/tree-sculpted-crown/integration.json`. The EXE reflects the shared workspace at export; the local commit is restricted to this tree commission.
