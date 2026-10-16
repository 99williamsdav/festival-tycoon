# Final copied-EXE verdict — accepted

16 October 2026. The repaired actual copied Windows executable is independently **accepted for the authorized tractor B playtest handoff**. No blocking findings remain. The initial P2 diagnostic save-isolation finding is closed by source inspection and actual-EXE verification. Initial review history follows this final verdict unchanged.

Final source commit: `b767f7e14f8d3456e9b3503fbb66561925413e8e`. Copied executable: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-tractor-b-2026-10-16/FestivalTycoon.exe`. SHA256: `2AEF156A996AEB24698D1F4751D139FF66D701FD7F64DF82856F15178F43081F`. Payload: 194 files, 304,381,007 bytes. The exact two-line repair makes Classic development diagnostics use the architecture evidence directory as a fallback and adds an explicit tractor host-directory assertion; no gameplay geometry changed in this repair.

## Final independent execution and evidence

Evidence root: `reports/evidence/tractor/independent-exe-final/`. Four sequential runs used the copied EXE directly, without `--path game` or `--fixed-fps`, through `Start-Process -WindowStyle Hidden -Wait -PassThru`. Each mode has retained `stdout.log`, `stderr.log`, and `exit-code.txt`. Application arguments after `--` were the named capture flag, absolute mode output directory, and `--capture-size 1280x720`.

| Mode | Assertions | Exit | stderr | Evidence |
|---|---:|---:|---:|---|
| `--capture-tractor` | 103/103 | 0 | 0 bytes | tractor/checks.json |
| `--capture-classic-stress` | 61/61 | 0 | 0 bytes | stress/checks.json |
| `--capture-survival-architecture` | 63/63 | 0 | 0 bytes | survival/architecture-evidence.json |
| `--capture-development-tiers` | 37/37 | 0 | 0 bytes | tiers/development-tier-evidence.json |

All four result files identify the exact copied executable path above. Total 264 passing assertions, zero failures. The final tractor run's development save directory assertion passes, and actual named live/finished/next/optional slots exist under `tractor/development-saves`. The immutable canonical transition is compared with the explicitly current-geometry reference by the development-tier harness, which passes.

Independently decompressed all evidence save files into compact `saved-state-inspection.json` summaries within each mode. Tractor and stress both record Classic geometry revision 1, live tick 11000 with 133 admitted people, and finished tick 46517 with 133 admitted/departed people and nine performance records. Neither live nor finished sample has NoRoute navigation action 3. This establishes sampled states, not absence of every transient route failure. Next festival remains revision 1. Management, Hands-on and Steward-only saved identities and current geometry are correct. The Survival harness passed three actual manual-steering cases, save/load identity/hash checks, isolated hosts, and exact parked-management restoration. Tier diagnostics passed preparation save/load and fresh/current launch checks.

Stress `clip-counts.json` contains all 420 rows: 100 attendees and 133 total people in every row, ticks 11004 through 12680. Full festival closure, nine records, and next-festival cash carry pass independently of that clip sample.

## Final visual coverage

Independently inspected all 16 tractor PNGs, all eight Survival PNGs, all eight development-tier PNGs, all 19 named stress UI PNGs, and stress clip frames 0000, 0140, 0280 and 0419. The other 416 clip PNGs were not individually visually reviewed. No blocking visual regression was found in this scope.

The tractor has the expected grounded blue appearance, unchanged measured mesh counts/bounds, opaque body and retained glass alpha. Ordinary solid/faded four-angle captures and default-62 views behave as previously reviewed. Converted capture 0 again has a solid roof despite its filename; captures 1–3 are visibly faded and show the stage kit without the tractor. Four-angle absence is supported by the visible entrance/interior views and presence/grid checks, but this is not four faded-roof captures. Optional inactive Barn retains the tractor. The live converted Barn has no tractor.

Stress UI screenshots show explicit diagnostic options, correct stage bookings/refund, construction pages, final results and next forecast. Live captures show all three stage areas. Some transient speech/finance overlays overlap world labels; final stress clip caption has clipped left text. These diagnostic/presentation limitations do not obscure authoritative counters or tractor acceptance, and no new UI/layout change or performance improvement is claimed.

## Final preservation and scope

Independently rehashed all checkpoint files both before and after the four final runs: **194/194 payload, 2,086/2,086 source, and 705/705 preservation pins match** (2,985 total). Evidence: `pins-before.json`, `pins-after.json`. This includes the 34 original task inputs, immutable Survival sources/baselines, older delivery pins, original approved tractor package, and all 14 default development-profile files pinned after the initial finding. `default-profile-after-all.json` confirms exactly 14 actual files, no unexpected files; those 14 hashes remained unchanged. This proves no further writes during final verification, not preservation before the initial unisolated run.

The initial diagnostic may have replaced existing development-test autosaves as well as its four named slots (and their backups); contents before that initial run are unavailable. Initial outputs and the stopped superseded stress attempt remain retained and are not relabelled as final passes. No product files, fixtures, baselines or original profile files were edited/restored by this reviewer.

These are scripted real-handler/simulation/save checks, not a manual full-day playthrough. The tractor remains a static prop; old geometry-zero saves are not silently migrated and have no tractor. Existing performance caveats remain. A requested 30-second tool sleep during final tractor execution returned a much longer wall duration; the cause was not established, and no elapsed-runtime or smoothness inference is made. No ZIP was created. Final process inspection found no copied-EXE process still running; the exclusive execution lane is released.

---
# Static blue tractor B — independent copied-EXE review

16 October 2026. Interim verdict: **not accepted pending bounded diagnostic isolation repair and review of the replacement copied EXE**.

Reviewed copied executable `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-tractor-b-2026-10-16/FestivalTycoon.exe`, SHA256 `71144FFE21BA9CA0D8E1C22DE0F65E5AA22AA149B234C31D044F6E9F2502B0AB`, against export checkpoint source commit `0df964217ef3c04d909a14c193ef31ea2f21bf35` (assets `8d73813d`, core `bb3ab899`). Fresh reviewer read WORKING-AGREEMENTS and source/native review, and independently inspected the evidence harnesses. No product, fixture, or immutable baseline edits; no rebuild by reviewer.

Before execution, independently hashed all 194 payload files, 2,086 source files and 691 preservation pins. All 2,971 matched. Evidence: `reports/evidence/tractor/independent-exe/pins-before.json`. Preservation groups include all 34 original task inputs; the development user-data folder is NOT pinned.

## P2 — tractor diagnostics write to the ordinary development-save folder

`game/Main.ClassicDevelopment.cs:13` isolates Classic development saves only when `_classicStressEvidenceOutput` is set. Tractor evidence sets `_tractorEvidenceOutput` and `_architectureEvidenceOutput`, so its Classic development lifecycle switches to `user://saves/r0-classic-development-v1`. This writes named diagnostic slots and autosave output outside the requested evidence directory. Bounded repair: have the helper use the architecture evidence output when applicable, and assert the development save directory in the tractor harness. Review the new actual export after repair; the current EXE is not final accepted delivery.

Observed actual folder: `C:/Users/99wil/AppData/Roaming/Godot/app_userdata/Festival Tycoon/saves/r0-classic-development-v1`. Named writes on 16 October: current-three-stage-live 10:45:36; current-three-stage-finished 10:46:58; current-next-festival 10:47:02; current-optional-barn-inactive 10:47:04 (local time). Read-only inspection and SHA256s are in `tractor/saved-state-inspection.json`; post-run directory listing is `tractor/development-folder-after.json`. No pre-run baseline or backup for this folder was established, so prior content preservation cannot be certified. Reviewer did not delete or restore these files.

## Initial copied runtime results

All commands invoke the copied EXE directly with `Start-Process -WindowStyle Hidden -Wait -PassThru`, stdout/stderr redirected into the mode output directory. Arguments are `-- --capture-tractor <absolute output> --capture-size 1280x720` (or the corresponding capture mode). No `--path game` and no `--fixed-fps` were supplied.

Tractor run: process exit 0, 102/102 assertions passed, stderr empty. `tractor/checks.json` reports the exact copied executable path. Imported geometry is seven meshes, 34 surfaces, 14,962 triangles, min (-1.127,0,-1.86), max (1.127,2.7545,2.19), world origin (0,0,-20.9). All 16 generated PNGs independently inspected. Four ordinary solid and four ordinary faded headings show the grounded approved blue tractor; solid roof naturally occludes rear views. Body remains opaque under shell fade; no visible cab-glass sorting break at captured resolution. Both default-62 captures show its honest small size. Optional inactive barn contains the tractor; the three-stage live image has converted barn without it.

Converted captures are four angles, but `converted-faded-0.png` actually shows a solid roof; the other three visibly show faded interiors and stage kit. Do not describe all four as faded. Front opening plus three interior views and authoritative presence/grid assertions establish conversion absence; this capture cadence caveat is not a product blocker.

Independent decompression of saved files confirms current geometry revision 1 and expected Management, Hands-on, Steward-only and Classic identities. Classic development live tick 11000 has 133 admitted, zero departed; final tick 46517 has all 133 admitted/departed and nine performance records. Neither saved sample contains navigation action NoRoute (3). Next festival retains revision 1. These are sampled states, not a claim that no transient route failure ever occurred. Actual harness file load/identity/hash roundtrips and legacy-zero mesh absence all pass.

Classic stress run was already active when isolation finding was reported; coordinator directed completion of that run, then release of the execution lane before Survival/development-tier runs. Final stress and after-pin results will be appended. Those other two modes are not yet run against this initial EXE.

## Limits and retained history

Scripted menu/command/time-step evidence is not a manual full-day playthrough. No new smoothness/frame-rate claim. Static prop, original cab-glass alpha, current geometry and legacy no-migration behavior remain the accepted source/native scope. Earlier source-review interrupted test invocation and Builder incomplete-copy/export history remain retained in their existing logs; no acceptance is claimed for those attempts. This review ran the completed checkpoint payload, not the incomplete copy. Initial diagnostics and this finding must remain visible when replacement EXE evidence is added.

## Initial lane closed

Coordinator subsequently directed stopping the superseded stress run after the reviewed repair was available, to avoid duplicating its remaining work. Reviewer identified exact process PID 28448 by executable path and full `--capture-classic-stress` command line, then stopped only that process. Start-Process returned exit -1; 265 PNGs and all partial outputs/logs are retained, with `stress/interrupted.txt`. **No stress pass is claimed.** Survival and development-tier modes were not started on this initial executable. A process query confirmed no remaining instance of this copied EXE; execution lane released for repair export.

After-run pin verification is `independent-exe/pins-after-initial.json`: all 194 initial payload and all 691 preservation pins still match; 2,084/2,086 source pins match. The two expected mismatches are Main.ClassicDevelopment.cs and Main.TractorEvidence.cs, changed concurrently by the root's source-reviewed bounded repair commit b767f7e1. No baseline was repinned by this reviewer. Root reports an archive of the initial full export and checkpoint, and a prospective backup/pin of 14 current development-profile files; that backup cannot establish pre-initial-run content.

