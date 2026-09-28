# Booking table v3 — minimal requested layout revision

Concept precedes implementation. The coordinator relayed the explicit user request: LINEUP/timetable left, available bands right; remove rating-header subtitles and numeric totals beside stars. This is the complete scoped revision, with no additional design gate or gameplay change. V2 files remain preserved. No game files were changed.

## Exact visual delta

1. Move the entire lineup/timetable to the **left** and available-bands table/filter/status to the **right**. Keep stage title **Trailer Stage**, existing per-slot × removal buttons and any other existing close controls. The concept shows × on its one occupied slot. Do not reintroduce the earlier concept's “Main stage” or “Remove selected act” text.
2. Table headers show only **Band, Genre, Price, Popularity, Ego, Professionalism**, plus existing sort arrows. Remove the visible second-line subtitles “Audience appeal”, “Demandingness”, “Softens ego reaction”. Retain these meanings in hover/focus help and accessibility.
3. Ratings show **five stars only**, including genuine half-star fills. Remove the numeric star total beside each strip. Preserve the exact raw 0–100 score and displayed star count in tooltip/accessibility; stars are not a new scale for sorting or simulation.

Everything else follows `../booking-table-concept-v2/HANDOFF.md` unless explicitly superseded here. In particular, this supersedes V2's instruction to show numeric totals and permanently visible header subtitles. The concise bottom meaning/status lines remain unchanged. No new icons, assets, rating logic, controls or gameplay rules.

## Geometry retained, columns exchanged

| Viewport | Left timetable x / width | Gap | Right table x / width |
|---|---|---|---|
| 1280×720 | 36 / 360 | 24 | 420 / 824 |
| 1920×1080 | 36 / 664 | 24 | 724 / 1160 |

Keep the V2 panel footprint: x16/y72, width viewport−32, height min(viewportHeight−138,660). Table header remains y246–294; vertically centre its now-single line. All six rows remain y294–546, 42px each. Retain all six full columns, no scrolling or truncation at either size. Column widths remain 188/96/68/136/136/200 at1280 and260/130/100/170/180/320 at1920. Do not shrink typography just because secondary text was removed.

Left timetable lane x90/y246/height300. Preserve the actual set times, changeovers, hitboxes, labels and title. The column swap must move the real controls and hitboxes together, not just their rendered labels. Keep the existing footer/Start/readiness footprint and top HUD/bottom controls unchanged.

## Tooltip, keyboard and semantics contract

- Each rating cell exposes band, metric, exact score, rounded star count and meaning on **hover and keyboard focus**, with equivalent accessible name/description. Example: “Barnstorm Circuit — Ego 35/100, 2 stars. Higher means more demanding/headline-sensitive.” Do not depend on colour or pointer hover alone.
- Header hover/focus help: Popularity “Audience appeal”; Ego “Demandingness; higher means more demanding/headline-sensitive”; Professionalism “Softens ego reaction; does not prevent it.” Preserve authoritative wording/behaviour, not a new popularity effect.
- Sort by raw scores, integer price, or displayed name/genre as before. Preserve initial Price ascending, stable ties, genre filter, selected/assigned state, row drag, keyboard placement and safe cancellation.
- Keep exact **“Expects to headline”** and authoritative Ego>=70 predicate. No “Expects set 3”. Removing visible numeric totals must not alter half-star rounding, expectation or reaction logic.

These SVG/PNG files are static layout concepts, not interactive runtime tests. Hover/focus help must be implemented and verified in Godot.

## Minimal acceptance checks

At1280×720 and1920×1080: lineup left/table right; six full rows/six columns; no internal scrolling; no rating subtitles or numeric totals visible; five-star strips including halves; current × controls and “Trailer Stage” intact. Check raw-score tooltip via hover and keyboard focus, alternate sort, Rock filter, selected/assigned/high-ego row, and drag/drop hitboxes after swapping columns. Preserve lineup/cash/simulation state under sort/filter/hover.

Proofs: `booking-1280.png`, `booking-1920.png`, `booking-1280-filtered.png`; corresponding editable SVGs and `render.cjs`. Source generator retains native star shapes and all six real catalog rows. No image-generation or new asset suite was used.
