# Field notes v1: production art

This is the art for the first-time signposting notes, from the approved board
(`Documents/Festival Tycoon concepts/field-notes/`): card A (the notebook page), the doodle pins, the striped wasp marker
and the field guide spread.

Rebuild with `python build_field_notes.py` (Pillow). It writes `out/*.png`, plus `out/field_notes_sprites.json` (sizes and
anchors) and `out/preview_card.png` (a card assembled only from these pieces and the spec below). Then copy the PNGs, not
the previews, to `game/assets/ui/field-notes/` (copies in `assets/runtime/ui/field-notes/`) and run the Godot import.
`fn_kit.py` holds the shared drawing code (doodles, pin, palette) and is also used by the concept board.

The colours are the existing `Ui` values. Paper `f5ebd6`, PaperRule `e2d3b3`, PaperEdge `d5c39e`, Gold `d8a43b`,
GoldInk `6b4f16`, Ink `1f2a26`, InkMuted `56615a`, outline `1d2a25` and wasp yellow `f2b233`.

**Every sprite is @2x.** Draw it at half its pixel size at 1280 × 720 (`Ui.S(px / 2)`). All the figures below are in
1280 × 720 mockup pixels.

## Files (`game/assets/ui/field-notes/`)

| File | Size | Use |
|---|---|---|
| `pin_<id>.png` × 10 | 60 × 76 | World pin per note id (`wasps heat drink stuck toxic power tempers late litter dusk`). The tip is at pixel (30, 66). |
| `pin_glow.png` | 96 × 96 | Dusk halo behind a pin, centred on the pin's disc centre. |
| `wasp_marker.png` / `wasp_marker_16.png` | 32 / 16 | Striped badge over allergic guests. |
| `doodle_<id>.png` × 10, `doodle_unknown.png` | 128 | Ink doodles for the card and guide rings. `unknown` is the "?" for blanks. |
| `ring.png` | 92 | Hand-drawn gold ring (card and guide). |
| `ring_blank.png` | 100 | Dashed ring for unseen guide entries. |
| `torn_edge.png` | 256 × 14 | The card's torn bottom edge. Tiles horizontally, period 256. |
| `tape.png` | 156 × 44 | Washi tape strip, unrotated; the alpha is in the texture. |
| `guide_gutter.png` | 56 × 8 | The guide spread's centre gutter. Tiles vertically. |

The pin, glow and marker imports are lossless with mipmaps, and `detect_3d/compress_to=0`, so a Sprite3D doesn't switch
them to VRAM compression. The other pieces use the default 2D import.

## World sprites

- **Pins:** 30 × 38 on screen. Use a Sprite3D (billboard, no depth test, fixed size) with render priority 3, and keep
  the current ±0.15 m bob.
  - The tip is 28 texture px below the texture's centre, so offset the sprite up by 28 px to put the tip on the anchor.
  - The anchors are unchanged: bins +1.4 m, people +2.6 m, toilets +3.4 m, the generator +2.6 m.
- **Glow (dusk only):** 48 × 48 on screen at about 45% alpha, behind the pin (render priority 2), centred on the pin's
  disc. The disc centre is at pin pixel (30, 26), which is 40 texture px above the tip.
- **Wasp marker:** 16 × 16 on screen, centred, +2.3 m, no bob, render priority 2. Fade it in and out over 0.3 s.
- **Rules from the board:**
  - The wasp note pins the bins only. Allergic guests get the marker at once, not a second pin 12 px away.
  - A group gets one pin on the highest head rather than one per person. The board's dashed ground ring (1.7 m radius)
    is a mesh or decal and isn't in this set.

## Note card (card A), 460 px wide

- **Body:** a StyleBoxFlat in Paper with radius 0 and a 3 px Gold top border (no other borders).
  - Content margins: left 22, top 20, right 22, bottom 14.
  - Shadow: `0a1410` at 40%, size 9, offset (0, 5).
- **Torn edge:** `torn_edge.png` directly under the body, the full 460 width, 7 px tall, tiled at half scale (@2x).
- **Left column:** `ring.png` at 46 × 46, top-left (22, 26). The doodle is 37 × 37, centred in the ring.
- **Margin rule:** a 1 px vertical line in `e7b7a0` at x 74, from y 8 to the bottom.
- **Text column:** x 84 to 438 (354 wide).
- **Header (y 20):**
  - `Ui.Caps("Field note", GoldInk, 10.5)`, then 6 px later `Ui.Caps("·  " + title, Ink, 10.5)`, both with 1.1
    letter spacing.
  - The counter "n of 10" is Source Sans SemiBold 11 in InkMuted, right-aligned at x 438 and centred on y 26.
- **Line:** `Ui.Slab` (Zilla Slab SemiBold) 18 in Ink, line height 23, first line at y 42, wrapping at 354. Two lines
  covers every current note.
- **Rules:** 1 px PaperRule lines from x 78 to 446, every 23 px, starting at y 61 (the first baseline). They stop 6 px
  above the bottom.
- **Footer:** 12 px below the last line.
  - "Kept in your field guide": Source Sans 12 in InkMuted, centred on the button's height.
  - "Got it": a `ButtonKind.Primary` button 84 × 30, right edge at x 438, at 13.5.
- **Tape:** `tape.png` at 78 × 22, rotated −7°. Its top-left is at (342, −12), so it overhangs the top edge. Draw it
  above the shadow.
- **Height:** 20 + 22 + 23 × lines + 12 + 30 + 14, plus the 7 px edge. That's about 157 for two lines.
- **Position:** unchanged: top centre at `ContentTop + 8`.

## Field guide spread (modal, 1280 × 720)

- **Dimmer:** `0e1f1a` at about 59% over the field. Blurring the field behind is optional.
- **Cover:** a StyleBoxFlat in `2a4a3e`, radius 12. It extends 10 px past the pages left, right and bottom, and 6 px
  at the top.
- **Pages:** a StyleBoxFlat in Paper, radius 6, rect x 140 to 1140, y 60 to 670.
- **Gutter:** `guide_gutter.png` tiled vertically, 28 px wide, centred on x 640.
- **Rules:** PaperRule at 47% alpha, every 24 px from y 180, inset 30 px from the page edges and the gutter.
- **Header:**
  - "Field guide" in Zilla Slab Bold 30 at (176, 90).
  - Subtitle "Notes from the field, kept as you find them." in Source Sans 14, InkMuted, at y 134.
  - The counter "n of 10 noted" in Source Sans Bold 15, GoldInk, right-aligned at x 1104, y 102.
  - Close: the HUD's round Bar button, 36 px, centred on (1122, 60).
- **Tape:** `tape.png` at 90 × 25, rotated +4°, at (540, 46).
- **Entries:** notes 1 to 5 in the left column (x 176), notes 6 to 10 in the right column (x 676). The first row's top
  is at y 168, with a row pitch of 98. For an entry at (x, y):
  - `ring.png` at 50 px, top-left (x + 3, y + 9). The doodle is 28 px, centred in the ring.
  - The text column starts at x + 70 and is 380 wide.
  - Caps "NN · TITLE" at 10.5 in GoldInk at y + 6.
  - The line is in Zilla Slab SemiBold 15, Ink, line height 20, from y + 26.
- **Unseen entries:**
  - `ring_blank.png` with `doodle_unknown.png`.
  - Caps "NN · NOT YET SPOTTED" in `a89a7a`.
  - Two rounded bars in `e6d8b8` (radius 4, 8 px tall) at y + 32 and y + 52, 92% and 60% of 380 wide.
- **Footer:** "Notes are remembered for you, not the save, so a new campaign won't repeat them." Source Sans 12.5 in
  InkMuted, centred on x 640 at y 656.
