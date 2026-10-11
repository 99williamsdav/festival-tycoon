# Steward shift report v1: Control's clipboard

The end-of-shift report card, from the approved Steward shift board
(`Documents/Festival Tycoon concepts/steward-shift/`): **option B**, the shift sheet on Control's clipboard, with a
rubber-stamped grade and a fights split bar. Grade scale: **Encore! · Headliner · Crowd-pleaser · B-side · Off-key**.

**Rebuild:**
1. `python make_report_art.py`
2. Copy `out/lwf_report_*.png` to `game/assets/ui/steward/` and run the Godot import.
3. `python compose_reference.py`

## Files

| File | Notes |
|---|---|
| `lwf_report_clipboard_v1.png` | The hardboard: warm brown `8a6a45` with grain, rounded corners, a darker edge |
| `lwf_report_clip_v1.png` | The steel clip with its spring housing |
| `lwf_report_sheet_v1.png` | Paper: `PaperBright` (`fff8e9`) with a faint grain |
| `lwf_report_stamp_{encore,headliner,crowd_pleaser,b_side,off_key}_v1.png` | Double-bordered rubber stamps with worn, blotchy ink and "STEWARD SHIFT · CONTROL". Teal for Encore! and Headliner, gold-shadow for Crowd-pleaser, alert orange for B-side and Off-key. |

All are 2× art. `INTEGRATION.md` gives the layout, the fights split bar, the grade colours and the timings.

**Fonts:** Zilla Slab and Source Sans 3 (OFL), from `game/assets/ui/fonts/`.

## Verification (`verification/`)

`compose_reference.py` draws the card **only** from the files above, at the spec's numbers, over the blurred
end-of-shift field:

| Render | Shows |
|---|---|
| `report-headliner.png` | 3 prevented and 1 broken up |
| `report-encore-no-fights.png` | The "Unplugged!" no-fights bar |
| `report-off-key.png` | Only fights broken up: an all-orange bar |
