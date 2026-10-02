# Farm beauty pass — v1

2 October 2026. Production assets from the approved concept (farm immersion 6). Project-original geometry,
authored procedurally in Blender 4.1.1 with a Pillow-generated palette; no downloaded meshes or textures.
Presentation only: no picking, navigation or simulation identity comes from these meshes.

Rebuild: `python make_palette.py`, then `blender -b --python build_farm_beauty_assets.py -- out`.
`out/farm_beauty_report.json` records tris, bounds, nodes, trunk radii and the pond footprint.

## Runtime files (`game/assets/environment/`, copies in `assets/runtime/environment/`)

All use one matte palette (`tex/farm_beauty_palette.png`, 128×8, 16 swatches, **Closest**, UVs at swatch centres):
slots 0–5 are foliage from a dark base to a light top, then hawthorn blossom, dog rose, haws/apples, wood/bark,
pond mud, reed, duck, drake head, bill and lily pad.

### 1. Hedgerows — drop-in replacements

| File | Tris | Notes |
|---|---:|---|
| `lwf_hedge_straight_8m_a_v1.glb` | 2720 | replaces the old slab |
| `lwf_hedge_straight_8m_b_v1.glb` | 3140 | new variant (replaces an unused archived runtime-only `8m_b`) |
| `lwf_hedge_straight_8m_c_v1.glb` | 2220 | new variant; one thin patch with a post |
| `lwf_hedge_straight_8m_d_v1.glb` | 2540 | new variant |
| `lwf_hedge_straight_4m_a_v1.glb` | 1340 | replaces the old 4 m run |
| `lwf_hedge_gate_end_v1.glb` | 740 | replaces the old gate end; rounded toward the gate |

These follow the same conventions as the kit they replace. Origin at the run's start on the hedge line, running
along local +X, at most ±0.5 m deep and 0–1.76 m tall. The 8 m and 4 m runs may overlap their neighbour by 0.1 m
at each end so joins have no gaps; the gate end stops exactly at x = 1.0, so it never enters the gate gap. Each
run is one mesh (`LWF_Hedge_MattePalette`), so the existing whole-node breeze shear works unchanged.
Blossom, dog rose and haws are part of the mesh. Seeded, so rebuilds are identical.

Godot will extract the new embedded palette as `…_farm_beauty_palette.png`. The old extracted
`…_hedge_palette.png` files beside the three replaced GLBs then become orphans and can be deleted.

### 2. Trees — origin at the trunk base; crowns are separate meshes for sway

| File | Tris | Height | Crown radius | **Trunk footprint radius** | Board position (Godot x, z) |
|---|---:|---:|---:|---:|---|
| `lwf_tree_oak_v1.glb` | 252 | 10.3 m | 4.6 m | **0.45 m** | (−32.0, 9.0), in the west hedge |
| `lwf_tree_field_maple_v1.glb` | 212 | 7.7 m | 3.2 m | **0.30 m** | (29.5, −29.5), NE corner behind the small barn |
| `lwf_tree_old_apple_v1.glb` | 452 | 4.8 m | 2.0 m | **0.18 m** | (−29.2, −6.6), farmhouse SW corner |

Nodes: `Trunk` (trunk and limbs) and `Crown` (canopy; the apples are part of the crown so they sway with it).
Block the cells within the trunk radius. At the default sun, the oak and maple each throw about 20–30 m² of shade
into the field (the heatwave hook from the board).

### 3. Pond — `lwf_farm_pond_v1.glb`, 381 tris, origin at the pond centre: place at Godot (24, 0, 24.5)

- `PondWater`: the water surface, with its own material `LWF_Pond_Water` (untextured, base colour #3E5E62,
  roughness 0.12), for day-light driving. About 8 × 6 m, at y 0.075.
- `PondMargin`: the mud margin (about 10 × 8 m, y 0.06), 70 reed clumps on the north and west (hedge) side, and
  7 lily pads.
- `Ducks`: two ducks (one drake), separate and optional.

**Footprint** (water plus muddy margin, to be made unwalkable and unbuildable). Margin polygon in Godot world x, z:
`(29.147,24.5) (28.971,25.911) (28.102,27.184) (26.765,28.236) (24.918,28.562) (23.044,28.73) (21.783,27.496)
(20.201,26.986) (18.802,25.976) (18.821,24.5) (18.849,23.038) (20.525,22.226) (21.519,21.148) (23.185,20.893)
(24.877,20.619) (26.544,21.063) (27.383,22.286) (28.379,23.257)`

As TraversalGrid cells (cell = floor((v + 64) / 0.5)): 289 cells, with any cell whose centre or a corner falls
inside the polygon counted as blocked. Each column ix has one continuous range of iz (inclusive):

```
ix 165:173-180  166:173-180  167:172-181  168:172-182  169:171-182  170:170-182  171:170-183
ix 172:169-184  173:169-185  174:169-185  175:169-185  176:169-185  177:169-185  178:169-185
ix 179:169-184  180:169-184  181:170-184  182:171-184  183:172-183  184:173-182  185:174-181  186:176-179
```

The full list is `blocked_cells` in `out/farm_beauty_report.json`.

### 4. Base grass drift — values for `Main.Ground.cs` (no asset)

Both terms multiply the tile's existing base colour (which keeps its own sage palette and facets), applied in
**linear** colour:
- **Slow tint drift:** world-space value noise at ~29 m feature size (Blender noise scale 0.035 on world
  position, detail 2). Remap the noise 0.30 → 0.70 to a multiplier that goes from **(0.86, 0.90, 0.84)** to
  **(1.04, 1.02, 0.94)**: slightly cooler and darker patches against slightly warmer and lighter ones, about ±7%
  in value.
- **Per-facet jitter:** a per-cell hash (Voronoi cells about 1.8 m across, close to the 2 m facet size) mapped to a
  uniform multiplier of **0.93–1.05**.

Keep both under concept 2's stripes, clover, margins and wear.
