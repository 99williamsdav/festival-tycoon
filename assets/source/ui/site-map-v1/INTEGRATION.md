# Site map v1: integration (B1 held laminated plan + A1 rotating corner minimap)

These are the approved site-map picks: **B1** (the held plan, raised pose only; B2's low pose is not included) and
**A1** (the always-on rotating corner minimap). Both apply to Steward Only and All-Rounder. No code has been changed.

**Conventions:**
- **HUD sizes:** 1280 mockup units, for `Ui.S`/`Ui.Px`. HUD art in `out/` is 2×, so draw it at `Ui.S(px / 2)`.
- **The plan texture:** measured in texture pixels.
- **Rotations:** Godot `RotationDegrees` (positive = clockwise on screen).
- **World:** metres, x east, z south; a cell maps to world as `c × 0.5 − 63.75`.

`compose_reference.py` is the reference implementation of everything below; `verification/` holds its output.

## What the map shows, and doesn't

**Shows:**
- **The fixed ground,** from the base art: field, hedges, gate, lane, farmhouse and barns, pond, the Trailer Stage, and
  the Pond Stage from Tier 2. Each stage has its crowd area outlined.
- **Every built stand,** from `CaptureBuildPlacements()`: taps, loos, bars, food vans, first aid, the steward post,
  bins and marquees.
- **Open radio calls,** gold, and **urgent calls,** alert orange.
- **You,** as an arrow pointing the way you face.
- **Hired crew** (stewards and maintenance), **in All-Rounder only:** teal when free, grey when busy.

**Never:** guests, or any incident Control hasn't called in.

## B1 · The held laminated plan

### The texture (1400 × 1276), redrawn per layout and tier

Render it in a `SubViewport` (1400 × 1276, `TransparentBg = false`). Assign its `ViewportTexture` as the albedo of
`LWF_FPPlan_Sheet` in `lwf_fp_arm_right_siteplan_v1.glb`.

**When to draw:**
- **Static layer:** once per day, and again when the layout changes.
- **Live layer:** whenever a call, crew position or your position changes. Crew and you can update at 4 Hz.

**World to texture:** `px = 40 + (x + 32) × 20.625`, `py = 150 + (z + 13) × 20.625`. The view is x −32 to 32, z −13 to
35.6, which is the festival area north to the barns and south to the gate.

**Draw order:**

| # | Layer | Spec |
|---|---|---|
| 1 | Base | `plan_base_t1_v1.png` or `plan_base_t2_v1.png` (Tier 2 adds the Pond Stage). It already holds the paper, the header rule (y 130), the ground and the fixed labels. |
| 2 | Header | "LOWER WITTERING FESTIVAL" in Source Sans 3 Bold 26, `InkMuted`, at (40, 34). "Site plan" in Zilla Slab Bold 54, `Ink`, at (40, 64). "TIER n · STEWARDS' COPY" in Source Sans Bold 22, `Alert`, right-aligned at (1360, 82). |
| 3 | Stand icons | `plan_icon_<kind>_v1.png`, centred on each stand (66 px; bin 46 px). Kinds: `tap`, `loo`, `bar`, `van`, `aid`, `post`, `bin`, `marquee`. |
| 4 | Stand labels | Source Sans 3 Bold 32, `Ink`. See the label rule below. |
| 5 | Control's notes | See below |
| 6 | Call stickers | `plan_sticker_call_v1.png` or `plan_sticker_urgent_v1.png` (88 px, centred on the call). Label in "marker" at (+33, −33), bottom-left anchored, in the pin's colour. |
| 7 | Crew (All-Rounder) | `plan_crew_free_v1.png` or `plan_crew_busy_v1.png` (40 px), with the first name in Source Sans Bold 27, `Teal` (`2b6e66`), at +24 |
| 8 | You | `plan_you_v1.png` (112 px), centred on you, with `RotationDegrees = heading` (0 = north = up; clockwise) |
| 9 | Legend | Row 1 at y 1174: the seven stand kinds (Water, Loos, Bar, Food, First aid, Bin, Post), icons at 32 px with labels in Bold 22. Row 2 at y 1222: radio call, urgent, you, and crew (All-Rounder only). "M  put away" right-aligned at (1360, 1239), `InkMuted`. |
| 10 | Laminate | `plan_laminate_v1.png`, a soft diagonal sheen, drawn last |

**Stand labels:**
- **Text:**
  - Water taps: "Tap", then "Tap 2".
  - Loos: one "Loos" label for the whole row, only on the stand whose id ends `.main`.
  - Bars: "Bar", "Bar 2".
  - Food vans: the trader's cuisine, e.g. "Chips", "Curry".
  - Also "First aid", "Steward post", "Marquee".
  - Bins: no label.
- **Placement rule:**
  - Draw all the icons first.
  - Then, for each label, try four spots in order: below the icon (centre-top anchor, r + 2 below); left of it (right-middle, r + 4); right of it (left-middle, r + 4); above it.
  - Take the first spot whose box overlaps no icon and no label already placed. If none fits, use below.

**Control's marker notes:**
- **Font:** the "marker" is Zilla Slab SemiBold 31 in `2b4a8c` (biro blue), slanted. Draw it with
  `draw_set_transform(pos, 0, Vector2.One)` plus an x-shear of −0.22 (top leans right), e.g. `Transform2D(Vector2(1, 0),
  Vector2(-0.22, 1), pos)`.
- **Font licence:** OFL, so no new font is needed.
- **Anchoring:** each note is centred on a point **anchored to something**, so it moves when the layout moves.

| Note | Anchor | Offset (m) | When |
|---|---|---|---|
| "IN / OUT" | The gate (0, 32) | (−6.5, −2) | Always |
| "keep the lane / clear for Henry" | The lane (−2, −9), with a short blue arrow pointing at the lane | (−7.5, 0) | Always (the tanker uses the lane) |
| "generator - / no guests!" | The Pond Stage generator (14.25, 18) | (+12.75, −7) | Tier 2+ |
| "chips queue / gets long here" | The first food van | (+6.75, +6.75) | Tier 1 |

**Call sticker text:** Control's call in two or three lowercase words, with no full stop.
- Collapse: "collapse!"
- Fight: "fight!"
- Loo engaged: "loo 2 stuck?"
- Tap: "tap burst"
- Bin: "bin full"
- Lost child: "lost child"

A sticker peels off (scale 1 to 0.6 and fades, 0.25 s) when the call is answered or closed.

### The pose and behaviour (`game/assets/characters/fp/lwf_fp_arm_right_siteplan_v1.glb`)

**The model:**
- A child of the eye camera at identity, like the other fp arms.
- Nodes: `LWF_FPArm_Right_SitePlan` (forearm, wristband, gripping hand, hardboard, clip; 320 tris) and
  `LWF_FPPlan_Sheet` (2 tris).
- The sheet is 0.39 × 0.355 m and centred at (0.109, −0.045, −0.397) in camera space. Its UVs run 0–1 with
  v = 1 at the top.
- A default plan texture is embedded, so it shows something before the `SubViewport` is wired.

**Raising and lowering:**

| Moment | Root offset | Timing |
|---|---|---|
| Rest, hidden | `REST = (0.10, −0.62, 0.12)` | n/a |
| Press M: raise | REST to (0, 0, 0) | 0.25 s, ease-out cubic `e = 1 − (1 − t)³` |
| While held | Stays at (0, 0, 0) | Add the radio arm's walk bob × 0.5 |
| Press M again: lower | (0, 0, 0) to REST | 0.18 s, ease-in cubic |

**Rules:**
- **Toggle:** M toggles it.
- **While it's up:**
  - **Walking** speed is **60%**, and running is off.
  - **Suspended:** the hold-to-act prompt card and every right-hand action (calm, open, pick up, help up), and the
    radial radio menu (Q).
  - **Still live:** the look (free), the radio strip and answering calls (R), the compass, the needs bars and V.
  - **Action keys:** pressing an action key (E) or Q **lowers the plan first** (0.18 s), then acts.
  - **Hint:** a centred hint under the compass at y 70–92 reads "WALKING SLOWLY · M to put away" (a `BarDeep` α 0.75
    pill, caps 11).
- **Switching to the manager view (V):** the plan lowers instantly.

**Verification:** `_held-up.png` and `_held-half.png` are the installed GLB through the 72° camera;
`hud-plan-up.png` and `hud-plan-raising-minimap-fading.png` show it in the HUD.

## A1 · The rotating corner minimap

**Placement:**
- A **176-unit circle**.
- **All-Rounder:** top-left corner at **(1264 − 176, 64)**, under the "V · Manager view" chip.
- **Steward Only:** at **(1264 − 176, 16)**, in the corner itself.

**Scale:** **3.143 units per metre** (radius 88 units ≈ 28 m).

**How it rotates:** ahead is always up.

| Element | Rotates? |
|---|---|
| Map layer | Rotates by `RotationDegrees = −heading` about you (heading = clockwise from north) |
| Stand icons | Positions rotate with the map, but the icons stay upright |
| You arrow | Fixed, pointing up |
| North tick | Rides the rim |

**Layers, inside a `Control` clipped by `mini_mask_v1.png`:** use `ClipChildren` with the mask on a `TextureRect`,
or a shader alpha mask.

| # | Layer | Spec |
|---|---|---|
| 1 | Backdrop | `BarDeep` α 0.78 disc |
| 2 | Ground | `mini_base_t1_v1.png` or `mini_base_t2_v1.png`: the whole 72 m field at 6.286 px/m (2×). (−36, −36) m is the texture's top-left, so you sit at px `((x + 36) × 6.286, (z + 36) × 6.286)`. |
| 3 | Stand icons | `mini_icon_<kind>_v1.png`, **16 units** (bin 11), centred, drawn only inside r 84 |
| 4 | Crew (All-Rounder only) | `mini_crew_free_v1.png` or `mini_crew_busy_v1.png`, 9 units |
| 5 | Call pins | `mini_pin_call_v1.png` or `mini_pin_urgent_v1.png`, 22 units, tip at the bottom centre on the call. **Beyond r 75**, clamp the pin to r 75 along its bearing and add a distance chip 24 units further in (towards the centre): a `BarDeep` α 0.82 pill, 16 tall, radius 8, with "24 m" in Source Sans Bold 10.5 in the pin's colour. Urgent pins pulse in scale 1.0–1.15 at 2 Hz. |
| 6 | You | `mini_you_v1.png`, 22 units, at the centre, pointing up |
| 7 | Frame | `mini_frame_v1.png`, the `Paper` α 0.43 rim, unclipped |
| 8 | North tick | `mini_north_v1.png` (18 units), centred on the rim at r 78, at angle −heading from the top |

**Icons:** the minimap uses the same icon language as the plan (`ICON` kinds), as light glyphs on dark discs at
16 units.

**While the plan is up:** the minimap fades from 1.0 to **0.0** over the raise (0.25 s), driven by the same eased value
so they cross-fade. It fades back in over 0.25 s after the plan lowers. Don't just dim it: the plan replaces it.

**Verification:** `minimap-t2-allrounder`, `minimap-t2-steward-only`, and `minimap-t1-facing-ssw` (north at the bottom
right) are standalone. In the HUD: `hud-minimap-allrounder`, `hud-minimap-steward-only`, and `hud-minimap-with-action`,
where the prompt card and right hand rise from below without touching the minimap.
