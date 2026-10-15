# Minimal Classic loop — Windows handoff, 15 October 2026

Status: APPROVED. Fresh independent core/UI reviews and the separate review of the actual copied Windows EXE passed. The scoped minimal Classic loop and shared dismissible save warning are ready for playtest.

## Delivered build

C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-loop-2026-10-15/FestivalTycoon.exe

Keep data_Festival.Game_windows_x86_64 beside the EXE. No ZIP was created. The previous Survival architecture delivery remains intact. The copied payload has 194 files, 285,218,019 bytes; EXE SHA256 is 88C0365E8DBAE770416543997F59E3E8E0EB645A37E71D1DC75E2C1294A8EFA6. The adjacent README explains entry and provisional rules.

Source: core 81ec8d8fd3548d960b015744cd8145db4c79bc48, UI 0765aeb, generated script IDs bb6750724452a103ebcd8c7a97844f1d7524fbd2. The export checkpoint pins the exact payload, 1,206 source inputs and export log. Existing unrelated character-import/package-lock edits were preserved and recorded; this delivery does not claim a clean checkout. Export completed with exit 0.

## Player behavior

The existing opening menu now offers Classic alongside Management Only, All-Rounder and Steward Only. Those three retain Survival rules and their separate saves. Classic starts on the farm with the Trailer Stage and repeats festivals without fixed-tier/Barn advancement. Current cash, parked debt, reputation, scenes, learned acts, relationships, popularity, owned kit and eligible previous layout carry through their explicit rules; daily crowds, work, incidents, rentals and other temporary state reset. The normal four-festival core reference closes with £276.40, £369.80, £475.95 and £565.35, with the actual £200 parked debt. Owned kit carry is supported; this power-budget loop rents rigs and does not invent a purchase route.

The approved provisional Classic defaults are fixed mild/dry weather, half normal disorder-pressure buildup and twice the existing medical, injury and generator response/death windows. Every causal death remains recorded, applies one −10 reputation penalty (floor zero, no fine), releases the deceased person's active claims and permits living actors to continue. Results distinguish admissions from surviving guests rated, foreground deaths and show net reputation. Repeated deaths, deceased staff/performers and an all-guests-dead completion have bounded test coverage.

Classic retains its own in-memory host and uses user://saves/r0-classic-v99. Continue, manual load and new-campaign reset operate on that host. Returning through Survival and back resumes the exact parked Classic session.

The shared save failure warning has X dismissal and a small Retry save control. An unsuccessful checkpoint reports failure without blocking an accepted start, next festival, return to menu or live play. Repeated failures stay dismissed for that episode; successful retry clears it. Earlier valid save bytes and current accepted in-memory state remain intact.

Stage expansion, reputation unlock thresholds, stage prices, rental placement, land unlocks, forecast/random weather, night, new art, loans/grants and death fines remain outside this commission. The separate future stage decision remains LATER.

## Validation and reviews

Fresh independent Astra MEDIUM baseline, core and UI reviews approved their scoped slices. Core review findings were repaired and narrowly rechecked. The final UI repair independently passed 101 native assertions with ordinary project stretch at 1920×1080, including actual pointer menu open/close and complete menu/bell bounds. See the linked owning review reports.

The expanded Classic core group passed 11 cases, including four paid completions/three continuations, explicit carry/reset/strict restore, once-only penalties and casualty cleanup. Final immutable Survival parity plus shared save host passed 8 cases: all 63 complete state/hash/ordered-event/RNG boundaries and six save-host cases. Original Survival fixtures and tests remain byte-for-byte unchanged from baseline 0055f741; no exclusions or repinning were introduced. These bounded groups overlap earlier runs and are not a claim that the full repository suite was run.

Builder ran the actual copied EXE in isolated output directories:
- builder-exe-classic: 101 passing assertions, zero failures, exit 0, nine PNGs.
- builder-exe-survival: 63 passing assertions, zero failures, exit 0, eight PNGs; evidence records the copied executable runtime path.
- builder-exe-warning: 24 passing assertions, zero failures, exit 0, four PNGs.

Classic's first GUI launch returned before completion in PowerShell; the running process handle was then retained, waited and its actual exit code 0 recorded. The other copied runs use Start-Process -Wait -PassThru. Diagnostic namespaces are isolated from player saves. No player save folder was reset or migrated.

Native progression follows real handlers and paid commands; specific menu/X/Retry checks use viewport pointer hit testing. Death presentation uses explicitly labelled overdue-medical injections without editing cash, reputation or results. These checks establish deterministic behavior and presentation, not natural-death balance, broad manual player QA or performance.

Fresh independent copied-EXE review APPROVED all three tracks: Classic 101/101, Survival 63/63 and save warning 24/24, each exit 0. The reviewer inspected all 21 PNGs and checked the state journal, logs and launch records. Classic used ordinary 1920×1080 project stretch; before/after all 194 payload and 1,206 source pins matched. No actionable findings remain in this scope. See classic-loop-exe-independent-review.md. The reviewer records the pre-existing rainy Survival top-bar overflow at forced 1280×720 as a retained layout limitation; this commission does not claim exhaustive Survival resizing acceptance.

## Evidence

- Core implementation/review: briefs/results/classic-loop-core-implementation.md and classic-loop-core-independent-review.md.
- UI implementation/review: briefs/results/classic-loop-ui-implementation.md and classic-loop-ui-independent-review.md.
- Shared warning implementation/review: briefs/results/save-warning-implementation.md and save-warning-independent-review.md.
- Immutable references: reports/evidence/classic-loop/survival-before/ and survival-before-checkpoint.json.
- Final payload/source/export pins and copied executions: reports/evidence/classic-loop/final/.

