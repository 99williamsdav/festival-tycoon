# Rain ground v1: integration

No code has been changed. Godot metres. The ground grid is 0.5 m cells, as in `GroundRules`.

## Straw: a ground-map state

**State:** add a straw channel per ground cell, for example in a third texture or the spare channel of the wear map.
`straw` is 0 for none and 1 for laid. `trample` (0 to 1) grows with footfall on straw cells and resets when straw is
topped up.

**Gameplay** (from the board):
- A cell with straw doesn't churn to mud.
- Fully trampled straw (`trample` = 1) stops protecting it, which is the cue to top it up.
- Laying it in build mode costs straw bales. Staff can also spread it on existing mud.

**Tiling rules:**
- **Unit:** the player paints straw in 0.5 m cells, so paths can be any width. Three cells (1.5 m) is the natural
  path, as on the board.
- **No special pieces:** straights, turns, Ts and ends need none. The shader frays every edge, so corners round off and
  ends break up by themselves.
- **Box blur:** blur the straw channel over 3 × 3 cells before sampling it, so cell steps never show.
- **The board's straw:** sample `lwf_straw_fresh_v1` and `_trampled_v1` at **world xz / 3 m**, and
  `lwf_straw_breakup_v1` at **world xz / 6 m**.

**Shader** (the same rule as `straw_reference.py`). Add it to the ground shader after the mud and puddle mix:

```glsl
uniform sampler2D straw_map : filter_linear;        // R = straw (0..1), G = trample (0..1), per 0.5 m cell, 3x3 box-blurred
uniform sampler2D straw_fresh : source_color, filter_linear_mipmap, repeat_enable;     // lwf_straw_fresh_v1.png
uniform sampler2D straw_trampled : source_color, filter_linear_mipmap, repeat_enable;  // lwf_straw_trampled_v1.png
uniform sampler2D straw_breakup : filter_linear_mipmap, repeat_enable;                 // lwf_straw_breakup_v1.png
// p = world xz in metres; uv_map = the same mapping wet_map uses ((p + 32.0) / 64.0)
vec2 sm = texture(straw_map, uv_map).rg;  float s = sm.r;
vec3 n = texture(straw_breakup, p / 6.0).rgb;
float e = s + (n.r - 0.5) * 0.70 + (n.g - 0.5) * 0.55 + (n.b - 0.5) * 0.30;   // ragged, uneven-width edge
float body = smoothstep(0.40, 0.52, e);
float tuft = smoothstep(0.64, 0.70, n.g) * smoothstep(0.04, 0.30, s);          // clumps spilling up to ~0.6 m into the mud
float fray = smoothstep(0.70, 0.74, n.b) * smoothstep(0.10, 0.35, s);          // single strands poking out
vec4 st = mix(texture(straw_fresh, p / 3.0), texture(straw_trampled, p / 3.0), sm.g);
float a = max(max(body, tuft), fray) * st.a;
c = mix(c, lin(st.rgb), a);
ROUGHNESS = mix(ROUGHNESS, 0.85, a * (1.0 - wetness * 0.5));                    // straw stays matter than wet mud
```

**Optional depth:** where `body` is above 0.9, a few standing tufts (a cheap MultiMesh of crossed quads, one per
1.5 m²) sell the fluffiness of fresh straw up close. Leave them off at zoom 24 and above.

**Staff spreading:** while a cell's straw rises from 0 to 1 over about 2 s, the rule animates the spread by itself.
Drop a broken-bale prop at the end of a staff run if one is wanted later; it isn't part of this package.

## Track matting: `lwf_track_mat_grid_v1.glb`

**Panel:** 2.0 × 1.0 m, 4 × 2 cells, top at 3 cm. Origin at the ground centre, long side on X (across the path),
short side on Z (along the path). The walkway is **2.0 m wide (4 cells)**.

**Gameplay:** matted cells never churn. Mud and puddles stay beside the path but never on it. Matting can be laid over
existing mud: the panel covers it, and the cells stop churning.

**Tiling rules:**

| Case | Rule |
|---|---|
| Run along Z | Panels at yaw 0, centres at (x, z0 + 0.5 + k), k = 0, 1, 2, …: one panel per metre of run |
| Run along X | Panels at yaw 90°, centres at (x0 + 0.5 + k, z) |
| Corner or T | The run laid first covers the 2 × 2 m junction square. The other run starts at the square's edge. No special corner piece, no overlap. |
| Ends | Square ends, with no ramp. At 3 cm it reads as flat flooring. |
| Grid | Run lengths are whole metres, and centres sit on the 0.5 m grid, so every panel snaps. |
| Rotation | Panels only ever turn by 0° or 90°. A 180° flip is identical. |

Lay the panels with a `MultiMeshInstance3D` per material (one mesh, many transforms). A long path is a few hundred
triangles.

**Wetness:** the mat joins the shared prop sheen (`heavy-rain-v1` INTEGRATION §4).

**Readability:** charcoal on grass reads at every zoom; at 62 it's a clean dark ribbon. The gold lugs only read close up.
