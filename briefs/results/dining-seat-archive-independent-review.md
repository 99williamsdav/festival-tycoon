# Independent dining-seat archive review — PASS

17 October 2026. Fresh, narrow independent review of the pending archive commit against `docs/WORKING-AGREEMENTS.md` and the explicitly approved archive-and-commit scope. No blocking findings. This review approves archive preservation and scope only; it does not repeat or extend the preserved independent production review.

## Checkpoint and verification

- Repository baseline HEAD remains `2ecb3e5cf286ed6070ecefbc0d4f43d0d5b13d22`. Archive commit is pending; no future commit SHA is claimed.
- Independently enumerated all 80 files under `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/dining-seat-production-2026-10-17` and calculated SHA-256 for each original and corresponding archived file. All 80 match byte-for-byte and match the 80 entries in `reports/evidence/dining-seat-archive/package-pins.json`.
- Independently checked all 67 entries of the original `PROVENANCE-sha256.json` against both original and archived files. All match. The original manifest and production `REVIEW.md` remain unchanged.
- Independently recalculated all 4,468 hashes in `preservation-before.json`: no missing or changed files. All 4,468 original status lines remain present; none disappeared or changed status.
- Archive contains 82 files: the 80 unchanged originals plus `INTEGRATION-STATUS.md` and package-local `.gitattributes`. The latter uses `* -text` to preserve exact file bytes through Git despite repository autocrlf behavior. Independently compared each of the 82 staged blob IDs from `git ls-files -s` with `git hash-object --no-filters` on its working file: zero differences.
- Inspected the staged design-index diff: exactly one appended archive entry. The 87 staged paths are exclusively the 82 archive files, `assets/source/DESIGN_INDEX.md`, and four archive evidence files. Comparing current status against the pre-task status found exactly those 87 new status entries and no unexpected paths. This report is an additional review artifact written after that snapshot.

## Explicit approved production hashes

- H1 `runtime/ft_hay_bale_h1_v1.glb`: `642d91afe7808986bac05b38476bee786917373618f7cd92c0ec299eac75ed09`.
- P1 `runtime/ft_picnic_aframe_p1_v1.glb`: `ded217a490181b3fd4e87941b78b0d2ae9d623e346248404d815c1e93872cf9a`.

Both independently calculated archive hashes equal the explicitly approved values.

## Scope and qualification

The parked integration note clearly identifies the two furniture-only production GLBs, editable production sources, and occupied review scenes/static fit evidence. It explicitly disclaims runtime seated animation and traversal approval, preserves the seating/access caveats, and defers runtime integration. The design index communicates the same distinction.

No new game/src code, runtime registration, live game assets, build or EXE changes appear in the reviewed change set. The `runtime/` subdirectory here is nested in the source archive, not a game registration or deployment. Existing dirty work remains intact. No Blender, native game, build, export or gameplay QA was run or required for this archive-only review.

`git diff --cached --check` reports original-package whitespace/CRLF diagnostics. These are non-blocking for the explicitly byte-preserving archive: original content was independently confirmed unchanged and should not be normalized as part of this task. No product changes were made by this reviewer.
