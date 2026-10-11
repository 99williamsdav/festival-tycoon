# Steward shift report v1: integration (report card B, the clipboard)

No code has been changed. All measurements are in 1280-wide mockup units (`Ui.S`). `compose_reference.py` builds the
card from the installed art at exactly these numbers; see `verification/`.

## Art (`game/assets/ui/steward/`)

All PNGs are drawn at 2×, so draw each at half its pixel size in mockup units.

| File | Pixels | Mockup size | Use |
|---|---|---|---|
| `lwf_report_clipboard_v1.png` | 1040 × 1388 | 520 × 694 | The hardboard. Transparent outside its rounded corners. |
| `lwf_report_sheet_v1.png` | 952 × 1268 | 476 × 634 | The paper (`PaperBright` with a faint grain). Text is drawn over it. |
| `lwf_report_clip_v1.png` | 300 × 140 | 150 × 70 | The steel clip, drawn over the sheet's top edge |
| `lwf_report_stamp_<grade>_v1.png` | 600 × 240 | 300 × 120 | One per grade, worn ink, unrotated |

## Screen

- **Background:** the field behind, Gaussian-blurred (about 6 px), with `BarDeep` (`0e1f1a`) over it at α 0.55. This
  is the same treatment as the results paper.
- **Header,** top left: "END OF SHIFT" in caps 10, `Gold`, at (36, 34); "Steward's report" in `Ui.Slab` 22, `BarText`,
  at (36, 50).
- **Buttons:** "Next shift" and "Return to menu", the results paper's gold buttons, at (1076, 590) and (1076, 646),
  188 × 46, radius 8. Labels in body bold 15, `Bar`.

## The clipboard

**Board:** top-left at **(380, 18)**, 520 × 694. Under it, a black α 0.47 shadow offset (4, 6), blurred about 8.

**Sheet:**
- Inside the board at (22, 44), 476 × 634.
- **Rotated 1.2° clockwise** about its own centre: Godot `RotationDegrees = 1.2`, `PivotOffset` = half its size. (Corrected: this read "−1.2°" before.)
- All sheet coordinates below are **sheet-local**, before the rotation. Put the sheet's contents in one `Control` and
  rotate that.

**Clip:** centred on the board: its left edge at board x + 185 (260 − 75), its top at board y + 2. Draw it above the
sheet.

**Stamp:**
- The grade's PNG at **0.6** of its 300 × 120 mockup size, so **180 × 72 units**, rotated **14° counter-clockwise**, tilting up to the right: Godot `RotationDegrees = −14`. (Corrected: this read "+14°" before.)
- Centred at board-local **(408, 114)**, the sheet's top-right corner, across the header rule.
- Draw it above the sheet.
- **On reveal:** start it at scale 1.6 and α 0 and "thump" it down to 0.6 and α 1 over 0.18 s, with ease-in back. Add a
  2 px screen shake on landing. It appears 0.6 s after the sheet slides up.

## Sheet contents (sheet-local)

| Element | Spec |
|---|---|
| Title | "Steward shift report" in `Ui.Slab` 28, `Ink`, at (24, 30) |
| Subtitle | "Steward 4 · Saturday · 18:00 – 21:00 · Lower Wittering" in body 12.5, `InkMuted`, at (24, 66) |
| Header rule | y 90, x 24 to 452, 2 px, `Ink` |
| Stat rows | Six rows at y = 108 + 44·i: icon 18 px `Teal` at (28, y + 6); label body 15 `Ink` at (58, y + 6); value `Ui.Slab` 20 `Ink`, right-aligned at 448, y + 4; row rule 1 px `PaperRule` at y + 36. Icons in order: `radio`, `clock`, `shield`, `triangle-alert`, `users`, `trash-2`. |
| Fights title | `Ui.Slab` 17 at (24, 384) |
| Fights split bar | See below: y 412–426 |
| Grade title | `Ui.Slab` 17 at (24, 470) |
| Grade pills | y 496–520 (24 tall), radius 12, 9 px padding each side, 6 px gaps, body bold 12.5. The awarded grade is filled `Ink` with `BarText` text; the others are outlined `PaperRule` with `InkMuted` text. |
| Control's note | "CONTROL'S NOTE" caps 10 `InkMuted` at (24, 548); up to two lines of body 13.5 `Ink` at y 564 and 584; "~ Control" in `Ui.Slab` 16 `InkMuted`, right-aligned at (452, 612) |

**The six stats:**
- Calls answered ("9 of 11").
- Average response ("0:48").
- Fights prevented.
- Fights broken up.
- Crowd mood ("72%  (from 64%)").
- Litter picked up.

## Fights split bar

- **Shape:** x 24 to 452 (428 wide), y 412 to 426 (14 tall), radius 7.
- **Track:** `AlertWash` (`f8dcc3`).
- **Prevented share:** `Teal`, from the left, width = 428 × prevented ÷ (prevented + broken up), with its own
  radius-7 ends.
- **Labels** at y 432, body semibold 12: left, "3 prevented before a punch" in `Teal`; right-aligned, "1 broken up" in
  `Alert`.
- **No fights at all:** the whole bar in `PaperRule`, with one centred `InkMuted` label: "No fights on your shift.
  Unplugged!"
- **Only broken up:** the bar is all `AlertWash`, and the left label reads "0 prevented before a punch".

## Grades

| Grade | Stamp ink | Suggested score |
|---|---|---|
| Encore! | `Teal` (`2b6e66`) | 90 and up |
| Headliner | `Teal` | 75–89 |
| Crowd-pleaser | `GoldShadow` (`9c7424`) | 55–74 |
| B-side | `Alert` (`b8551e`) | 35–54 |
| Off-key | `Alert` | below 35 |

**Suggested score (0–100):**

| Component | Weight |
|---|---|
| Calls answered ÷ calls | 35% |
| Response time: 100% at ≤ 30 s, 0% at ≥ 3 min | 20% |
| Prevented ÷ (prevented + broken up); 100% if there were no fights | 20% |
| Crowd mood change over the shift: +10 points of mood = 100% | 15% |
| Litter: 15 pieces = 100% | 10% |

**A missed urgent call caps the grade at B-side.**
