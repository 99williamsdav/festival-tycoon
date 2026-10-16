# Static blue tractor B — integration handoff

16 October 2026. Source/native and repaired actual copied Windows EXE independently accepted.

## Delivered behavior

The approved cabbed blue tractor is grounded at barn-local (0,0,2.1), front +Z, identity scale/rotation. It appears in ordinary barns. One shared `BarnTractorPresent` predicate controls presentation, layout validation, traversal and movement; an active Barn Stage removes both mesh and obstruction, including optional Classic selections. Existing stage kit, signs, lighting and surroundings retain their behavior.

Navigation covers the full wheel/bonnet/ballast envelope, expanded once by the existing upright actor's 380 mm body clearance. It blocks 88 grid cells and checks crossing sweeps in autonomous movement, separation and player input. The artist's optional 150 mm margin is not added again. Both barn doors and the remaining floor retain access.

The tractor is separate from the fading barn shell: body materials remain opaque and original cab-glass alpha is retained. Native import measured seven meshes, 34 surfaces, 14,962 triangles; local bounds (−1.127,0,−1.860) to (1.127,2.7545,2.190), world origin (0,0,−20.9). Runtime GLB is unchanged from approval: SHA256 `2C501B42D2B440F8F5623281F4F3426FDD6261E3F1FA04E56DB74A0B557A187D`. Original Blender source, export, scripts, palette, manifest and asset reviews are archived under `assets/source/environment/blue-tractor-asset-v1/`.

## Save compatibility

Fresh production Management, Classic, Hands-on, Steward and development launches explicitly select persisted/hashed barn geometry revision one. Loaded legacy revision-zero campaigns and immutable recorded fixtures retain their original geometry, with no tractor mesh or obstruction. There is no live-save migration. Retry, next festival and Classic rebooking preserve the originating revision. Start a new current campaign to see the tractor. Unknown revisions and inconsistent current saved grids are rejected. Historical immutable fixture/test sources were not rewritten or repinned.

## Verification

- Builder broad regression: 44/44 passed, including initial ten tractor cases, Barn campaign allocation, Classic construction/booking and immutable Survival parity. Final targeted run: 15/15, adding lifecycle, malformed-grid, rollback and real manual movement checks. This is 49 distinct cases across those runs. Fresh independent source reviewer separately passed all 15 targeted cases.
- Real held player movement stops before the tractor and crosses clear adjacent floor. Seven optional stage masks, current/legacy roundtrips, real Council retry, canonical next festival, rejected-command rollback and save-write failure behavior are covered.
- Game build passed with zero warnings/errors. Repaired Windows export completed successfully. Logs, failed diagnostic attempts and test details are preserved under `reports/evidence/tractor/validation/`.
- Builder source-native run: 102/102 assertions, exit zero, empty stderr; all 16 PNGs inspected by Builder and source reviewer. Actual mode hosts, parked-host exact hash, file roundtrips and a complete 133-person, nine-set festival were exercised.
- Repaired actual copied EXE: tractor 103/103 assertions, exit zero, empty stderr; all 16 images independently inspected. All current named saves restore exactly, 133 people admitted/departed, nine performance records and zero NoRoute at live/final snapshots. Development saves are inside the supplied evidence directory; all 14 default development-profile files remain unchanged from the pre-repair snapshot.
- Final Classic stress: 61/61, exit zero, empty stderr. All 19 named UI images and four sampled clip frames independently inspected; all 420 count records contain 100 attendees / 133 people. Live/final saves use geometry one; final tick 46,517 has all 133 people admitted/departed, nine records and zero NoRoute in both checked snapshots. Survival host flow passed 63/63; development tiers passed 37/37. All four final runs exited zero with empty stderr: 264/264 checks. Independent reviewer inspected all 51 named final images plus four sampled clip frames; the remaining 416 clip frames were not visually inspected. See `briefs/results/tractor-exe-independent-review.md` for acceptance.

Source review: `briefs/results/tractor-integration-independent-review.md`. Native evidence: `reports/evidence/tractor/builder-native/` and `independent-exe-final/tractor/`.

## Windows package

`C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-tractor-b-2026-10-16/FestivalTycoon.exe`, with adjacent `data_Festival.Game_windows_x86_64`.

EXE SHA256 `2AEF156A996AEB24698D1F4751D139FF66D701FD7F64DF82856F15178F43081F`. Payload: 194 files / 304,381,007 bytes, excluding README. All copied bytes match the repaired export. Product commits:

- `8d73813d` — approved asset archive/runtime import.
- `bb3ab899` — persisted geometry, authoritative navigation and tests.
- `0df96421` — presentation, production launch boundaries and native evidence harness.
- `b767f7e1` — diagnostic save isolation and explicit runtime path assertion.

`reports/evidence/tractor/export-checkpoint.json` pins 194 payload, 2,086 source and 705 preservation entries (2,985 checks). Preservation includes previous Classic stress, herding and r2 payloads, older EXEs, original 34 dirty/untracked inputs, ten immutable references, the original 35-file approved package and the 14-file development-profile snapshot. Some historical preservation groups overlap; these are pin counts, not unique-file counts. All 2,985 checkpoint hashes matched independently before and after the four final runs, including all 14 default development-profile files; no unexpected file was found there. All 35 archived original package files also match the original package byte for byte. Export uses the working tree with the pre-existing 27 tracked edits and seven untracked UID files preserved.

## Retained diagnostics and limits

Initial copied-EXE review found a diagnostic save-isolation defect: the tractor capture wrote development-test autosaves and four named capture slots under the default `user://saves/r0-classic-development-v1` profile. Its contents were not pinned before the first run, so prior development-test autosaves may have been replaced; their original contents cannot be certified from the available evidence. Ordinary campaign profiles use separate paths. No files in that development profile were deleted or restored. Its current 14 files were backed up under `artifacts/tractor-development-profile-before-repair/` and pinned before repaired testing; unchanged-after-repair proves no further writes, not preservation before the initial diagnostic.

The independent reviewer approved the two-line source repair; the rebuilt tractor capture explicitly verifies its isolated host path. Initial EXE/data and checkpoint remain in `artifacts/tractor-initial-export/` and `validation/initial-export-checkpoint.json`. Initial tractor passed 102 checks; superseded stress was intentionally interrupted and has no pass claim. Earlier build typing failure, invalid bin-in-barn test setup, interrupted wrong-filter reviewer test and premature incomplete copy were corrected; their records remain in validation. No acceptance is claimed for superseded/incomplete attempts.

The supplied four converted angles show absence: first final converted capture caught the solid roof before fade settled, while the other three show the interior through faded surfaces. Filenames retain the original `converted-faded-*` labels; actual roof state is stated here. Solid roof/default zoom naturally obscure much of the tractor. No visible cab-glass sorting break was found at the supplied resolution. Some captures contain transient overlays, and a late sampled stress clip caption is clipped; these are diagnostic capture caveats documented by the reviewer. Native scripts use real handlers and simulation with explicit stepping and labelled starting fixtures; this is not a manual full-day playthrough.

Static prop only: no driving, animation, new prices/unlocks, arrival/departure schedule change or performance fix. Existing performance limitations remain; no new smooth frame-rate claim. Older deliveries remain available. No ZIP or other-chat message.
