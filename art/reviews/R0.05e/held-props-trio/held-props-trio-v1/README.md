# Held food/drink trio v1 — one approval checkpoint

Awaiting exact coordinator and user approval. No repository/runtime copy, gameplay edits, integration or commit. All character context is adult. The drinks stall is separately approved; that approval does not extend to these props.

Original chunky low-poly geometry and flat muted materials follow the countryside/festival direction in GAME_DESIGN_SPEC.md: kraft cardboard, golden chips, coloured paper cup and cream lid, simple translucent beer cup/amber liquid/pale foam. No brands, downloaded models, external textures or AI raster artwork.

## Exact files and scale

Editable source: `lwf_held_props_trio_v1.blend`; recipe: `build_held_props_v1.py`.

| Runtime candidate | Full bounds W × D × H | Triangles | SHA-256 |
| --- | --- | --- | --- |
| `lwf_chips_tray_v1.glb` | 183 × 123 × 60.85 mm, including chips | 212 | `0bb9ebb82a2b3558a450beb84d9b8f89467c5b67e603a1c759c69cb0d95167a3` |
| `lwf_soft_drink_cup_v1.glb` | 94 × 94 × 217 mm, including straw | 240 | `b0570c93ea5cba3c7d297111e3c070d66ac4e0f6cd24edede37c4e3b77fe9416` |
| `lwf_beer_cup_v1.glb` | 92 × 92 × 152 mm | 252 | `cea01a1956768d0792fe4dfd7698f0beb9f52ea899dc78cca8f28cfa7efa5dd9` |

Cup body height is 150 mm. Tray nominal open vessel is 180 × 120 × 50 mm. Each GLB has one mesh node, applied transforms, metric units, glTF Y-up and no animation, rig, collision, arms or context. Source SHA-256: `897752204a90ca099c8c19e9d4f41905849277b3da63765149407051e93b4e8d`.

Cup origins are at the body mid-height; tray origin is at the centre of the nominal tray volume, with underside at Godot local Y −0.025 m. These are presentation grip-centre proposals, not a verified socket. Builder owns eventual adult attachment/pose. No hand or arm is baked into the prop export.

## Review views and limits

- `01-held-props-trio.png`: equal-scale enlarged review, beer / soft drink / chips left to right.
- `02-adult-hand-scale.png`: existing 1.75 m adult model unchanged, props adjacent to neutral hands for physical scale only. This is not a verified grasp or holding animation.
- `03-game-camera-32m.png`: 1920 × 1080 with 32 m horizontal orthographic framing and approximately 38.9° elevation. Blender framing/scale review, not an integrated game screenshot. Cups are only about 5–6 pixels wide here; tray about 11 pixels. Colour and broad silhouette help, but chips/foam/straw detail cannot be reliably read at this distance. Inspector text remains needed for exact product identity. Do not silently enlarge physical props to hide this limitation.

Final renders visually inspected. `verify_held_props_v1.py` passed source reopen and three GLB reimports with matching node count, bounds and 212/240/252 triangles. Godot 4.7.2 isolated scratch import and instantiated-scene inspection passed; 5/3/4 material surfaces respectively. Clear shell alpha 0.22 and rim 0.65 import as transparent materials; opaque amber and foam require no transmission shader. In-game transparency sorting, lighting, picking and hand attachment remain unverified. Host certificate/user-log/editor-setting permission warnings occurred during successful isolated import; no permissions were bypassed.

Only the snake-case GLBs listed above are the current candidates. Early filename variants and `.blend1` are local iteration backups, not approved handoff files. Stop for both exact approvals; no further prop variants or gameplay/economy decisions implied.
