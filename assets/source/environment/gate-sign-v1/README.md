# Gate apron and gate sign tiers — v1

2 October 2026. Production assets from the approved concept (farm immersion 3a, all three tiers). Project-original
geometry and textures, authored procedurally in Blender 4.1.1 and Pillow; no downloaded meshes or textures.
Presentation only: no picking, navigation or simulation identity comes from these meshes.

## Runtime files

`game/assets/environment/` (and the approved copies in `assets/runtime/environment/`):

| File | Tris | Nodes |
|---|---:|---|
| `lwf_gate_apron_v1.glb` | 309 | `GateApron`, `Lane` |
| `lwf_gate_sign_tier1_v1.glb` | 64 | `GateSignTier1`, `GateSignTier1_Board`, `LetteringArea` |
| `lwf_gate_sign_tier2_v1.glb` | 89 | `GateSignTier2`, `GateSignTier2_Board`, `Pennants`, `LetteringArea` |
| `lwf_gate_sign_tier3_v1.glb` | 632 | `GateSignTier3`, `GateSignTier3_Board`, `Pennants`, `LetteringArea` |

## Placement

Every asset shares one origin: the gate centre line, on the ground, at the hedge line. Place each root at **Godot
(0, 0, 32)** with no rotation. Local +Z points down the lane, away from the farm.

- **Apron:** a 16 m wide island running from z = −0.5 (tucked under the hedge) to about 9 m beyond the hedge line.
  It has an irregular rounded outer edge, a grass lip and a 0.45 m tapered soil skirt, so it reads as a deliberate
  little island against the sky. The lane (4 m wide, two wheel ruts, track palette) continues from the gate to the
  island edge. Grass top is at y = 0.03 and the lane at 0.052, matching the field tiles and the track.
- **Tiers 1–2** stand on the apron verge left of the gate, turned 32° toward the default camera and the lane.
- **Tier 3** is an arch spanning the lane at local z ≈ 3.2. Posts are at x = ±3.7 (outside the lane), planters at
  ±5.0. The board's underside is at 3.4 m and the pennants' lowest point about 3.2 m.

None of it touches the gate leaf, the track inside the farm or buildable ground.

Apron revision (in-game review): the apron now uses the field/track palette itself (`tex/field_track_palette.png`,
a copy of the grass tile's embedded palette) as `LWF_FieldTrack_MattePalette`. Its grass is a 2 m triangle grid on
the tile's world grid, constrained to the island outline, using all six tile greens with the tile's 0–0.05 m
height variation. The lane copies the vehicle track's exact cross-section: light edges (slot 8), mid fill (slot 9)
and dark ruts (slot 10) at the same widths and heights. The island edge and soil skirt are unchanged.

## Materials

- Signs: `LWF_GateSign_MattePalette`, a 96×8 embedded palette with **Closest** filtering and UVs at swatch centres, the same
  approach as the other farm assets. Slots 0–5 reuse the field/track palette colours exactly (grass 0–2, lane 3–4,
  soil 5), then wood, dark wood, green paint, gold, terracotta and cream.
- Board art (Linear filtering): `LWF_GateSign_Tier1_DoorArt`, `…Tier1_ArrowArt`, `…Tier2_BoardArt`,
  `…Tier3_BoardArt`, `…Tier3_WelcomePlate`. **No festival text is baked.** Only the tier-3 "WELCOME TO" plate
  carries text, and the tier-1 card carries a painted arrow but no words.
- Pennants are separate `Pennants` meshes, so they can take a breeze sway later.

## Runtime lettering

Each sign GLB contains a `LetteringArea` node at the centre of a clear, flat, rectangular area on the board face,
2 mm proud of it. Its local +Z faces the reader, +X is text-right and +Y is up, so a Label3D or quad parented
to it and laid in its XY plane reads correctly. The rectangle size is in the node's glTF extras (`width_m`,
`height_m`). The values below are in the asset's root-local Godot space; `out/lettering_areas.json` holds the
same data.

| Tier | Centre (x, y, z) | Size (w × h m) | Facing (+Z) | Text-right (+X) |
|---|---|---|---|---|
| 1 | (−5.397, 1.150, 2.604) | 2.20 × 0.78 | (0.530, 0, 0.848) | (0.846, 0.070, −0.529), board tilted 4° |
| 2 | (−5.135, 1.650, 2.440) | 2.48 × 0.78 | (0.530, 0, 0.848) | (0.848, 0, −0.530) |
| 3 | (0.000, 4.150, 3.385) | 4.00 × 1.15 | (0, 0, 1) | (1, 0, 0) |

Tier 2's area sits right of the painted sun; tier 3's sits between the two emblem roundels. Fit text to the
width (two lines, e.g. name on the first and "Festival" on the second, suit all three tiers).

## Suggested fonts (not downloaded; check each licence file before adding to the repo)

| Tier | Font | Licence | Why |
|---|---|---|---|
| 1 | Caveat Brush (Pablo Impallari) | SIL OFL 1.1 | Rough brush hand for the painted door; add a slight per-glyph jitter for wobble |
| 2 | Kalam Bold (Indian Type Foundry) | SIL OFL 1.1 | Neat casual brush script for a properly painted board |
| 3 | Playfair Display Bold (Claus Eggers Sørensen) for the name + Yesteryear (Astigmatic) for "Festival" | SIL OFL 1.1 (both) | Sign-writer serif caps over a traditional script; add a dark drop shade |

## Rebuild

```
python make_textures.py
blender -b --python build_gate_sign_assets.py -- <out_dir>
```

`make_textures.py` writes the palette and the text-free board art to `tex/`. The build script writes the four GLBs,
a `.blend` per asset and `lettering_areas.json` to the output folder (`out/` here).
