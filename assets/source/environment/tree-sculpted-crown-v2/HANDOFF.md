# Sculpted Crown A — oak and old apple

User approved A with “A 100%”. The selected board and exact imagegen prompt are in `references/`. Other concepts and earlier rejected iterations remain in the art workspace's `tree-detail-four-concepts` and `tree-detail-concept-v1` directories. The board is illustrative, not a native model render.

Original procedural Blender geometry, no external meshes or photo textures. `build.py` constructs overlapping volumes and fuses them through voxel remeshing, smoothing and decimation into a single faceted foliage skin. A muted matte palette supplies broad colours; there are no individual leaf shapes, leaf textures, transparency cards, bones or embedded animations. The apple has nine separate faceted fruits joined into its Crown mesh. Trunk/limb geometry is a separate Trunk mesh; upper limbs remain concealed.

Rebuild with Blender 4.1.1: `blender -b --python build.py`, then `blender -b --python verify_assets.py`, then `blender -b --python render_review.py`. Source `.blend` files, palette PNG, runtime GLBs, manifest, imported-GLB verification and four exported-GLB renders are retained in this package. `verification.json` checks topology after UV seam welding, low-trunk radius, triangle count, node names, material count and absence of skeletons/actions.

Runtime uses `lwf_tree_oak_v2.glb` and `lwf_tree_old_apple_v2.glb` under `game/assets/environment/`. Existing v1 files and source are retained. Placement is unchanged: oak (-32, 0, 9), apple (-29.2, 0, -6.6). Trunk clearance radius is unchanged at .45m / .18m. The oak has 1,522 triangles, the apple 2,262 including fruit, versus 252/452 previously. Both retain two mesh nodes with one palette material per mesh, preserving the existing Crown sway hookup.

Canopy bounds are fitted to the original tree bounds from the archived farm beauty manifest. Fruit projects a little beyond the apple canopy: about .12m maximum beyond the previous whole-asset box. Low trunk/root geometry remains inside the existing trunk radius; no navigation, building cells, gameplay shade rules, placement rules or collision nodes changed. The field maple, hedges, willow and grass are unchanged.

Rendering differs from the concept's illustrative light and proportions: native views use existing game scale and lighting, and the trunk remains slim enough for its existing footprint. In-game integration and measured evidence belong to `briefs/results/tree-sculpted-crown.md`. Never treat the board's farm insets as native evidence.
