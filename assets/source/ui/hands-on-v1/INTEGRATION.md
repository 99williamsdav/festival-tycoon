# Hands-on campaign v1: integration

These are the approved picks from the hands-on board: **1B** radial radio menu, **2A** "Your shift" results tab, and
**3A** title buttons (restyling the user's three modes), plus the view cues and the YOU marker. No code has been
changed.

**Units:** all measurements are **1280-wide mockup units**, the values `Ui.S()` and `Ui.Px()` take. The art in
`game/assets/ui/handson/` is **2×**: draw each PNG at `Ui.S(pixels / 2)`.

**Rotations:** given as Godot `RotationDegrees` (positive = clockwise on screen).

**Colours:** `Ui.cs` names, hex in brackets.

`verification/render.py` builds every piece from the installed art at these numbers.

## 1B · Radial radio menu (first person, hold Q)

### Layout

The menu is centred on the **crosshair** (screen centre, 640, 360). Draw order, back to front:

| Layer | Art | Size (units) | Placement |
|---|---|---|---|
| Backdrop | `lwf_radial_backdrop_v1.png` | 320 × 320 | Soft `BarDeep` disc, α 0.47 |
| Inner wedges | `lwf_radial_wedge3_<state>_v1.png` | 320 × 320 | Each sprite points **up**. Rotate about the centre: **0 = Send the medic (up), 120 = Call Dirty Henry (lower right), 240 = Send crew (lower left)**. |
| Crew fan (when open) | `lwf_radial_crew<N>_<state>_v1.png` | 500 × 500 | N = 1–6 hired crew; see the crew fan below |
| Hub | `lwf_radial_hub_v1.png` | 108 × 108 (r 54) | `BarDeep` with a hi-vis rim |
| Labels, needle | drawn in code | n/a | See below |

**Inner ring:** r **58–150**, three **112°** wedges with **8°** gaps. The hover sprite pops out to r 156.

**Wedge states:**

| State | Fill | Outline |
|---|---|---|
| normal (free) | `Paper` α 0.93 | white α 0.47, 2 |
| hover | `Gold` (`d8a43b`) | `GoldInk` (`6b4f16`), 3 |
| busy | `PaperRule` (`e2d3b3`), with faint `Alert` hatching | `Alert` α 0.63 |
| disabled | `Ink` (`1f2a26`) α 0.59 | dashed `BarMuted` (`b9c4b8`) |

### Wedge labels

Drawn in code, on the wedge's axis at **r 104** (crew wedges at r 199):

| Element | Spec |
|---|---|
| Icon | 20 px, centred, 30 above the axis point. Medic `heart-pulse` in `Alert`; Henry `truck` in `Teal`; crew `users` in `Teal`. On hover, `Bar`. |
| Name | `Ui.Slab` 13 (crew 12.5), centred on the axis point, `Ink` |
| Status line | Body bold 10, 17 below |

| State | Status line | Colour |
|---|---|---|
| Free | "FREE · 42 m" | `Teal` |
| Busy | "BUSY · back in 3 min" | `Alert` |
| Hover | "FREE · 42 m" | `Bar` (on gold) |
| Disabled (Tier 1, no crew hired) | **"No crew hired · hire in Staff"** | `BarMuted`, with the name in `BarText` |

### Hub

| Element | Spec |
|---|---|
| Caption | "CONTROL", caps 10, hi-vis `d8f126`, at y − 14 |
| Selection | The hovered entry's name in `Ui.Slab` 12, `BarText`, at y + 2 |
| Hint | "release to send", body 9.5, `BarMuted`, at y + 18 |
| Needle | A 3-unit `Gold` line from the centre to r 46, with an 8-unit dot at its tip, pointing at the virtual cursor |

### Crew fan (the second ring)

- **Opens** once the cursor has been over **Send crew** for 0.2 s, or straight away when it passes r 150 inside the
  crew wedge.
- **Shape:** a **180°** fan centred on the crew wedge (240°), so it runs from 150° to 330°. Radii **162–236**; the hover
  sprite pops out to r 242.
- **Wedges:** each spans **(180 − 6·(N − 1)) / N** degrees, with **6°** gaps. Wedge j is centred on
  **150 + span/2 + j·(span + 6)**.
- **Up to 6 crew:** free ones first, then by distance. With more than 6, the hub's hint reads "+2 more".
- **Labels:** at r 199. Name, then role or current job ("steward", "fixing a tap"), then FREE/BUSY with the distance.
  Icons: `shield` for a steward, `wrench` for maintenance.
- **While the fan is open,** the inner Send crew wedge stays in its hover state.

### Input

- **Open:** hold **Q**, after **0.12 s** (so a tap stays free for other uses). The menu fades and scales in from 0.85
  to 1 over 0.12 s.
- **Look damping: 100%.** The camera's look is **frozen** while the menu is open. Mouse motion drives a **virtual
  cursor** that starts at the centre, at the player's look sensitivity. It's clamped to r 150, or r 236 while the crew
  fan is open.
- **On close,** look resumes over a 0.15 s ease-in, with no jump.
- **Moving:** WASD keeps walking at full walk speed. Running is suppressed while the menu is open.
- **Picking:** the hover is the wedge whose angular sector holds the cursor's angle, once the cursor is past the
  **dead zone (r 24)**. Hover changes ease over 0.06 s, with a soft radio click.
- **Keyboard:** **1 / 2 / 3** pick the inner wedges directly while Q is held. With the crew fan open, 1–6 pick crew.

**Release Q:**

| Hovered when released | Result |
|---|---|
| A free medic, Henry or crew member | **Sends** them to the target |
| A busy entry | **Queues** the job; the strip reads "Henry: after this job · 3 min" |
| A disabled wedge | Nothing is sent. The wedge shakes ±4 units for 0.2 s and the hub shows its note. |
| Send crew, with no crew wedge hovered | **Cancels** |
| Anything inside the dead zone | **Cancels** silently |

- **Esc or right mouse** cancels at any time.
- **Close animation:** 0.08 s.

**The target:** the current radio call if one is open; otherwise whatever is under the crosshair, or your own position.

### Radio arm

The installed `lwf_fp_arm_left_radio_v1.glb` needs **no new GLB**.
- **While the menu is open:** offset the arm's root by the **menu raise (0.07, 0.08, 0.06)** in camera space, eased in
  over 0.15 s. The talk raise is (0.03, 0.05, 0.04).
- **After a send:** drop to the talk raise while the reply plays on the radio strip. The radio strip's own spec is in
  `steward-shift-hud-v1`.

**Verification:** `radial-hover-medic`, `radial-hover-busy-henry`, `radial-crew-open`, `radial-tier1-crew-disabled`,
`radial-released-sent`.

## 2A · "Your shift" tab on the results paper

### The tab

A fourth tab under Performances, at y = **70 + 3·52 = 226**, in the existing tab style: the `radio` icon (18) and
"Your shift" in body bold 15.

**The unseen dot:**
- A 12-unit circle at (tab right − 20, tab top + 8), filled `Alert` (`b8551e`), with a 2-unit outline in the paper
  colour (`f6f0e2`).
- Shown until the tab is opened for the first time on that results paper, then hidden.
- Shown again for a new grade.

### The page

The page lives inside the existing sheet (230–1060). All x/y below are screen units.

| Element | Spec |
|---|---|
| Title | "Your shift", `Ui.Slab` 30, `Ink`, at (260, 52) |
| Subtitle | Body 13, `InkMuted`, at (260, 92): "You on the radio · organiser and steward · 18:00–21:00" |
| Rule | y 118, x 260–1016, 2 px, `Ink` |
| Stat cards | y **134–218** (84 tall), four cards **180** wide with **12** gaps, from x 260, radius 6. Light cards: fill `fffdf6`, 1 px `PaperRule` outline. Caption caps 9.5 `InkMuted` at (+12, +12); value `Ui.Slab` 26 `Teal` at (+12, +26); line body 11.5 `InkMuted` at (+12, +62). |
| Light cards, in order | **CALLS ANSWERED** "9 of 11" / "avg response 0:48"; **FIGHTS** "3 · 1" / "prevented · broken up"; **MOOD YOU ADDED** "+8%" / "64% → 72% while on shift" |
| Dark grade card | The fourth card. Fill `Ink`; caption "CONTROL'S GRADE" in `Gold`; the grade name ("Headliner") in `BarText`; the line in `BarMuted` ("4 of 5 · litter 14"). |
| Calls on the night | Title `Ui.Slab` 19 at (260, 240). Rows at y = 272 + 30·i: time body bold 13 `InkMuted` at x 260; the call body 13.5 `Ink` at x 310; who went, right-aligned at x 640, body bold 12 with a 14 px icon to its left; a 1 px `PaperRule` rule at y + 24 (x 260–640). |
| Press quote | A block at (260, 440)–(640, 536), fill `efe6d2`, with a 4-unit `Ink` bar down the left. "IN THE PAPER" caps 9.5 `InkMuted` at (278, 452); the quote in `Ui.Slab` 19 `Ink` at (278, 468); the source body 11.5 `InkMuted` at (278, 510): "The Lower Wittering Gazette, page 2". |
| Mini clipboard | `lwf_results_mini_clipboard_v1.png` at **(684, 240)**: board, sheet and clip, with no stamp. |
| Stamp | The grade's `game/assets/ui/steward/lwf_report_stamp_<grade>_v1.png` at **0.4** of its pixels (**120 × 48** units), centred at **(885, 420)**, `RotationDegrees = −14`. Thump it in as in the report spec. |

**Who went:**

| Who | Label | Icon | Colour |
|---|---|---|---|
| You | "you went" | `user` | `Teal` |
| The medic | "sent the medic" | `heart-pulse` | `Teal` |
| Henry | "sent Henry" | `truck` | `Teal` |
| Crew | "sent Clive" | `shield` or `wrench` | `Teal` |
| Missed | "missed" | `x` | `Alert` |

**Quote examples:** "Organiser seen breaking up a scuffle", "Boss mops up at Bar 2", "Organiser first on the scene
of a collapse". Pick by the biggest moment of the shift.

**Verification:** `results-your-shift`, `results-tab-unseen-dot`.

## 3A · Title mode buttons (the user's three modes, restyled)

The names and straplines are the user's own, kept as they are.

### The row

Three buttons, **244 × 58**, **16** apart, centred, in the place of today's mode row (y 514 in the 1280 layout).

| Mode | Icon | Label colour | Strapline (ModeLine) |
|---|---|---|---|
| MANAGEMENT ONLY | `map` (newly installed) | `f3e8c9` | "Run the festival from the office." |
| ALL-ROUNDER | `user` | `e8f23a` | "Run it, and steward it yourself until you can afford the help." |
| STEWARD ONLY | `radio` | `e8f23a` | "Pull on the hi-vis for a single shift." |

### StyleBoxFlat

| State | Fill | Border | Notes |
|---|---|---|---|
| Normal | `15201f` | 2, `5d6a64` | MANAGEMENT ONLY uses `f3e8c9` as the default choice |
| Hover | `1d2b29` | 2, `Gold` | n/a |
| Pressed | `0f1716` | 2, `Gold` | n/a |
| Focus (keyboard) | n/a | n/a | An extra ring 4 units outside: 2 `Gold` α 0.63, radius 8 |

All corners radius 5.

**Content:**
- **Icon:** 22 px at (x + 16, vertically centred), in the label colour.
- **Label:** body semibold 18, at x + 48, vertically centred.
- **NEW tag on ALL-ROUNDER:** a 40 × 18 `Gold` pill, radius 9, at (x + 198, y − 9), with "NEW" in body bold 10, `Bar`.
- **Strapline:** under each button, body 13 in `b4bfb2`, centred, wrapped to 244. First line centred at y + 72; lines
  19 apart.

**Verification:** `title-modes`, `title-modes-hover-allrounder`.

## View-switch cues (the key moves to V)

The same chip in both views:

| Element | Spec |
|---|---|
| Chip | 36 tall, radius 8, fill `BarRaised` (`22453a`), 1-unit border `3b5a50`. Width = 60 + label width + 16. |
| Key cap | "V", 22, at (+7, +7) |
| Icon | 18 at (+36, +9) |
| Label | Body bold 14, `BarText`, at +60, vertically centred |

| View | Text | Icon | Placement |
|---|---|---|---|
| Manager | **"V · Walk the field"** | `user`, hi-vis `d8f126` | In the bottom bar, right-aligned to the rotate button's left edge (x 1108), y 668 |
| First person | **"V · Manager view"** | `map`, `Gold` | Top right, at (1264 − width, 16), beside the compass |

**Feedback:** on V the chip's border flashes `Gold` for 0.2 s. Suggested camera swoop: 0.6 s, with the world running.

**Verification:** `manager-you-tag` (manager chip), `fp-manager-view-cue`.

## The YOU marker (manager view)

| Piece | Spec |
|---|---|
| **YOU tag** | `lwf_you_tag_v1.png`, **60 × 38** units, screen-space (no zoom scaling). Its pointer tip sits 4 units above your steward's head (screen position of the head bone), so the tag's centre is 22 units above the head. Hi-vis `d8f126` with a `BarDeep` outline. |
| **Ground ring** | `lwf_you_ring_v1.png`, a top-down ring texture: a hi-vis outer ring, a fainter inner ring and four ticks. Put it on a `Decal`, or an unshaded flat quad 2 cm up, **1.2 m** across, centred on your feet. Pulse its scale 1.0–1.08 at 1 Hz. Hide it in first person. |
| **Edge badge** | Shown when your screen position is outside the view inset: 24 units from the screen edges, below the top bar (60) and above the bottom bar (64). Clamp the badge's centre to that inset rectangle. |

**Edge badge parts:**
- **Disc:** `lwf_you_edge_disc_v1.png`, 50 × 50 units, always upright.
- **Arrow:** `lwf_you_edge_arrow_v1.png`, 84 × 84 units, centred on the disc. It points right at 0°; rotate it to the
  direction from the screen centre to you.
- **Label:** "You · 85 m" in body bold 12, `BarText`, with a 1-unit dark shadow, centred 34 units below the disc.
- **Click:** centres the camera on you with a 0.4 s pan.

**Verification:** `manager-you-tag`, `manager-you-edge`. The edge render shows two example badge positions, right edge
and top.
