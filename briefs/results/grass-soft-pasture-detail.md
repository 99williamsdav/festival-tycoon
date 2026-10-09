# Soft Pasture detail refinement — 9 October 2026

Following the user's request for a touch more detail at the screenshot's farm zoom, `game/Main.Ground.cs` now adds subdued fine mottling and irregular half-metre tufts. The original tiny blade layer faded out at this distance; the new layer retains visible texture at farm zoom, with derivative-based edge smoothing and a fade at greater distance. The approved colours, clover drifts, flowers and existing wear/water/mud logic remain unchanged. The same shared material applies to the cow pasture. Only shader presentation and its outdated summary comment changed.

The game build passed with zero warnings/errors and the Windows release export completed. Actual exported 1920×1080 and 1280×720 fixtures both passed and reported unchanged authoritative simulation hashes. All four camera orientations, farm/close pairs and synthetic ground-state bands were captured; reviewed views include south/west farm, south close, the 720p south farm and synthetic bands. No manual festival playthrough or motion/shimmer certification is claimed. The preceding 24 ground/navigation tests remain the last simulation test run; no simulation code changed in this refinement.

[Evidence](../../reports/evidence/grass-soft-pasture-detail/) preserves the previous Soft Pasture field shader, representative before/after PNGs, build/export logs, fixture JSON and delivery hashes. The fixture's before field is commit `fd391b2`; its before pasture uses the original source material, so that pasture is not a matched comparison against the first Soft Pasture revision. Import-stage `get_multiple_md5` file-access errors occurred during character palette reimports; export continued successfully, and those unrelated imports are excluded from this commit.

| Resolution | Material | Median ms | p95 ms | Draw calls |
| --- | --- | ---: | ---: | ---: |
| 1920×1080 | Previous field | 3.2637 | 16.3141 | 659 |
| 1920×1080 | Refined | 3.2910 | 17.0856 | 659 |
| 1280×720 | Previous field | 2.8784 | 16.2530 | 659 |
| 1280×720 | Refined | 2.5850 | 15.6201 | 659 |

These are noisy whole-app wall-frame timings in a fixed preparation view, 30 warm-up frames then 90 samples each. They establish the same draw count, not a performance improvement or isolated GPU cost. No added geometry or texture assets.

Delivery: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-soft-pasture-detail-2026-10-09/FestivalTycoon.exe` with adjacent data directory. All 194 EXE/data files match the tested export by SHA-256. Previous playtest retained separately.
