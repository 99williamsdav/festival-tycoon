# Open drinks stall prototype v1 — first approval checkpoint

Awaiting separate coordinator and user design approval. On 26 September 2026 the user requested an asset checkpoint commit; this package is archived under `art/reviews/R0.05e/drinks-stall-prototype-v1/` as an unapproved review prototype, not an integrated runtime asset. No gameplay edits. Handheld chips/soft-drink/beer props have NOT been started; they remain gated after this stall's approval. All people are adult characters.

## Design decision and review

The existing service-point v2 was inspected. Its enclosed cabin and serving hatch are too heavy for the requested open-front bar, so this prototype is freshly authored while retaining its aged-timber, cream canvas, sage and dark-metal language. Four posts, low rear boards, an open counter, pitched striped canopy and painted DRINKS board provide a visibly different silhouette from the existing wheeled food van. No approved van or kiosk source was modified.

- `01-drinks-stall-preview.png`: close three-quarter view.
- `02-adult-food-van-scale.png`: existing 1.75 m adult attendee and repository-approved food van at authored metric scale, context only.
- `03-game-scale-32m.png`: 1920×1080 Blender review at a 32 m horizontal orthographic framing and the current game camera's approximately 38.9° elevation. This is a scale/framing review, not an integrated game screenshot. Signage should still be accompanied by selection/inspector text at distant zoom.

The available repository food van is `lwf_food_van_*_v1`, whose source/review show an angular towable shell; the drinks bar remains clearly distinct regardless. No rounded-van redesign is included. Context flap is shown closed; none of that context is in the stall export.

## Dimensions and proposed handoff

- Editable source `lwf_drinks_stall_prototype_v1.blend`; standard GLB `lwf_drinks_stall_prototype_v1.glb`.
- 654 triangles, four mesh nodes: Structure, Counter, Canopy, Sign. Packed/embedded original 1024×256 sign texture; remaining materials are matte flat colours.
- Total visual bounds **3.0888 m wide × 2.1713 m deep × 3.0525 m high** including canopy/sign overhang. Proposed solid reservation **3 × 2 m**, coordinated with Builder; small canopy/sign overhang lies beyond that reservation. Ground-centred origin, metres, applied transforms, glTF Y-up.
- Customer face: Blender −Y / Godot +Z; counter top **1.10 m**.
- Godot local service face centre: `(0, 0, 0.95625)`; customer-stop proposal `(0, 0, 1.75)`, 0.75 m outside the proposed front footprint edge. Presentation handoff point `(0, 1.10, 0.7125)`.
- Queue/FIFO sites, staff positions, collision, reservations and final world placement belong to simulation/integration, not these mesh anchors. No site is approved by this preview.

## Provenance and limitations

Original project-authored Blender primitives/custom canvas polygons, flat materials and editable `drinks-sign.svg` rasterized by `render-sign.cjs`. The sign is a flush painted surface, not floating letter meshes. Arial is a local system font; no font is bundled. No external model, photograph, texture or downloaded asset. Existing attendee/food van are review-only project context.

The source/reimport check is `verify_drinks_stall_v1.py`; bounds/anchor proposal are in `technical.json`. No collision, navigation, service identity, products, stock, animation or gameplay is embedded. No in-game lighting or picking validation is claimed at this gate. Builder reports no verified standard hand socket on the adult model, so later props will use grip-centred origins and independently reported physical dimensions; attachment will be presentation-owned after approval.

Stop here until both approvals. No handheld prop work or later assets yet.
