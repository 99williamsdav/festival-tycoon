# Tap flow icon trio v1 — review checkpoint

Awaiting separate coordinator and user approval of this exact contact sheet/trio. No game integration, gameplay changes or standpipe 3D edits. This local package mirrors repository asset/review conventions; it has not been copied into the game repository or committed.

## Review

`01-contact-sheet.png` shows LOW FLOW, NORMAL FLOW and BOOSTED FLOW at 160 px, actual 48 px, actual 32 px and a one-ink 32 px legibility check on the established cream paper surface. The 32/48 px colour samples are the actual exported PNGs, composited without resizing. Runtime icons have transparent backgrounds and contain no text.

## Files and use

- Editable original vectors: `assets/source/ui/lwf_tap_flow_{low,normal,boosted}_v1.svg`.
- Transparent runtime PNGs: `assets/runtime/ui/lwf_tap_flow_{low,normal,boosted}_v1.png` (128 × 128), plus `_32px.png` and `_48px.png` variants.
- Every icon uses a 64 × 64 source viewBox, shared tap silhouette and aligned outlet. Dark farm ink `#39453F`, water blue-green `#36747B`.
- LOW FLOW: two separated drops. NORMAL FLOW: one narrow continuous stream. BOOSTED FLOW: a broad three-strand continuous stream. The different silhouettes do not depend on colour.
- Display with adjacent game-authored textual labels. Do not bake text into the PNG or use the image as the sole accessible state label. Intended for the selected-tap context panel at 32–48 logical pixels, not a new world marker.

The icons depict the resulting flow presentation, not modifier identity. Council sharing and the tower may coexist; adjacent text should explain the baseline cap and tower bonus. The art does not prescribe state thresholds, imply a shared-pressure penalty, or invent a plumbing network. No zero-flow/unavailable state is included.

## Provenance and verification

Original project-authored SVG geometry, not traced from a photo, external icon library or downloaded art. Flat palette informed by existing farmhouse/standpipe and paper UI. Contact-sheet labels use the local system Arial font; no font asset is bundled. `render-icons.cjs` at the package root reproducibly rasterizes the SVGs using the bundled Sharp renderer. No external texture or network resource is required.

All nine PNGs were checked for exact dimensions and transparent alpha; the contact sheet was visually inspected at original resolution. `technical.json` records palette, silhouettes, sizes and SHA-256 source/runtime hashes for this review version. This checkpoint stops before integration or approval of another asset.
