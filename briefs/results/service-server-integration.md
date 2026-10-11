# Approved service servers — 11 October 2026

Integrated the user-approved A/A outfits from [the frozen handoff](../../assets/source/characters/service-server-assets-v1/HANDOFF.txt): cream/teal bar servers and sage/canvas food servers, male and female variants. Approval, concept, Blender source, build/verification scripts, role palettes and original references are preserved in the source packages. All five installed GLB hashes match the delivery manifest.

Each pitched vendor owns one cosmetic 17-bone rig, with existing head/hair, its delivered 16-slot role palette, scale 1 and the exact approved root/yaw. Default Tier 2 uses all four variants. The food chassis alone uses the minimal opened-hatch derivative; original chassis, livery, fascia/sign, kitchen dressing, counters and queue/footprint conventions are retained.

Current service owner/ticks drive turn, pickup and offer. Products appear only after pickup and while the authoritative order remains eligible. The actual successful transaction hides the server's item as the existing customer item appears, then triggers empty retract and a sparse interruptible reset after every fourth observed sale. Water uses the existing soft cup; ponchos have no invented held prop. Failed/cancelled/out-of-stock, unaffordable and beer-refused orders clear offered items. Skipped ticks seek the current pose; no event backlog or delayed purchases.

The presentation cursor uses stable transaction IDs and only the newest new receipt per vendor. New sessions/attempts and rebuilt vendors baseline existing history. State is unsaved and makes no simulation RNG calls. No actors, staff IDs, needs, hiring/costs, service durations, wallets, stock, queue, navigation, collision or picking rules were added. A read-only eligibility query reuses the existing authoritative sale checks. Save compatibility remains v64.

AnimationPlayer uses manual processing. Pause, preparation, terminal/results, start menu, challenge reveal, field guide and hidden vendors freeze playback, phases and prop timers. Resuming does not accumulate hidden time. Resources, imported clip lengths, players and all applicable prop instances are cached; there is no per-frame mesh/material construction.

## Verification

- Build: zero warnings/errors. Successful Windows release export; the delivered EXE and adjacent C# data directory contain fresh assemblies.
- [Nine focused tests](../../reports/evidence/service-server-integration/focused-tests.log): service progression and immediate handoff, deduplication/rebuild/load, bounded fast-forward, sparse reset interruption, freeze/resume, real out-of-stock/wallet/beer-refusal failures, and a physical day with save/load and unchanged authoritative hashes after every presentation update.
- Full non-Slow suite: 563 passed, 25 disk-write failures under filesystem restrictions, plus the documented unused TrafficProof `PerpendicularAndDiagonalCrossingsDischarge(True)` failure. [All 25 disk/save failures passed](../../reports/evidence/service-server-integration/disk-tests-recheck.log) when rerun with permitted filesystem access. The [original full log](../../reports/evidence/service-server-integration/sandbox-fast-suite.log) is retained. The existing single-stage behavioral digest passed; no economy or queue expectations were re-pinned.
- [Exported runtime assertions](../../reports/evidence/service-server-integration/exported/server-evidence.json): four owned rigs, 17 bones/scale/anchors, no new colliders, pause/menu/terminal/hidden exact freeze, small-time resume, actual relocation/rotation/removal/rebuild, and all six role/livery views. 363 native blend samples per view across applicable products; maximum foot drift below 0.000001 m, prop tilt below 0.44°, bar clearance at least 0.1748 m, food at least 0.2961 m. This tests feet and held props, not continuous triangle-level collision physics. Designer's sampled body/static-furniture collision evidence remains in the source package.
- [Release review gallery](../../reports/evidence/service-server-integration/review.html): 72 actual EXE captures, four camera orientations for all four rigs and chips/curry/pizza contexts, at inspection size 6, minimum size 18 and default size 62. Includes a short 30fps staged animation clip. These are labelled staged views, not live transaction footage or a manual playthrough.

## Measured cost and limits

On this machine, the exported vsync-off staged preparation sample (90 frames per condition) measured median whole-app frame time 2.0015 ms with servers hidden and 2.0675 ms visible; p95 18.3243/19.0656 ms, draw calls 753/813. This is a static visibility comparison of four rigs with the rest of the game present, not isolated GPU/animation CPU cost or live-festival FPS certification. Performance varies with view and machine. Four delivered bodies total approximately 8.7k triangles, plus existing props.

Existing food fascia/roof substantially hides heads/caps at the high camera; the rear van shell hides the server entirely. Bar canopy/sign also occludes faces from some angles. Default-zoom detail is small. Geometry and outfits were preserved; no roof/sign redesign or distorted body was used. No hardware-controller test is claimed.

## Delivery

Windows folder: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-service-servers-2026-10-11`, containing `FestivalTycoon.exe` and `data_Festival.Game_windows_x86_64`. Run the EXE with that folder beside it. No ZIP produced. Export log and release evidence are retained under `reports/evidence/service-server-integration`.

Unrelated pre-existing 22 texture import edits, lead-outfit source and two UID files were excluded from the narrow commit. Repository metadata was not repaired/reset. The sandboxed incomplete export was replaced by the successful export before delivery.
