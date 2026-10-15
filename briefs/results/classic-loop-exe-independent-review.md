# Classic copied-EXE independent review — 15 October 2026

Verdict: **approved for the scoped minimal Classic loop and shared dismissible save warning in the actual copied Windows delivery.** No new actionable findings. This is the separate copied-EXE gate following the approved core/UI reviews.

Reviewed docs/WORKING-AGREEMENTS.md, prior core/UI reviews, export checkpoint, relevant diagnostic code and the handoff draft. The draft accurately distinguishes source tests, builder evidence and deterministic diagnostic limits; its pending acceptance status can now be updated.

## Identity and integrity

Executed C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-loop-2026-10-15/FestivalTycoon.exe, with its adjacent data_Festival.Game_windows_x86_64 folder. EXE SHA256: **88C0365E8DBAE770416543997F59E3E8E0EB645A37E71D1DC75E2C1294A8EFA6**.

Before and after the independent runs, all **194 payload files / 285,218,019 bytes** matched reports/evidence/classic-loop/final/export-checkpoint.json; all **1,206 source file pins** also matched. Export log SHA256 matched **E94202E865CB1473CCD4B6D3892F0947322D17C449866933490E56E3E23D8051**. Pins identify core 81ec8d8fd3548d960b015744cd8145db4c79bc48, UI 0765aeb and generated metadata bb6750724452a103ebcd8c7a97844f1d7524fbd2. The recorded unrelated 25 import changes and package-lock change were preserved. No clean-checkout claim is made.

## Independent executions

All three runs were sequential after builder clearance. Each used Start-Process -FilePath $exe -ArgumentList $arg -Wait -PassThru -WindowStyle Hidden, with native execution approval and isolated evidence/save directories. Actual process exit codes, IDs and full arguments are retained in each launch.json. No player-save namespace was used, no export/build occurred, and no runtime source or delivery file was changed.

Common arguments: --fixed-fps 30 --quit-after 260 --log-file "ABS_OUTPUT/runtime.log" --

| Diagnostic arguments | Output under reports/evidence/classic-loop/final/ | Result |
| --- | --- | --- |
| --capture-classic-loop "ABS_OUTPUT" | independent-exe-classic | 101 passed, 0 failed; exit 0; 9 PNGs |
| --capture-survival-architecture "ABS_OUTPUT" --capture-size 1280x720 | independent-exe-survival | 63 passed, 0 failed; exit 0; 8 PNGs |
| --capture-save-warning "ABS_OUTPUT" --capture-size 1280x720 | independent-exe-warning | 24 passed, 0 failed; exit 0; 4 PNGs |

ABS_OUTPUT is the absolute repository path to the respective output directory above. Classic omitted capture-size and retained ordinary project stretch; its captures are 1920×1080. Survival architecture-evidence.json independently records the exact copied executable runtime path. All processes finished; zero FestivalTycoon processes remained at final verification. Logs contain completion markers and no runtime error report.

## Evidence assessment

Inspected **all 21 PNGs**, both checks.json files, architecture-evidence.json, Classic journal.json, launch records and logs. Classic offers a separate menu entry and host, four normal paid starts, three next-festival continuations, actual canonical carry and daily reset, no forced Barn/tier advance, real save-file/hash round trips, manual load, exact parked resume across Survival and fresh-campaign reset. The ordinary-stretch menu opens/closes through pointer hit testing; full menu/bell rectangles fit in first/fourth/death live states. The file named 02-classic-box-office actually shows the initial perk selection.

Journal evidence records three normal closed festivals and the fourth closed after two explicitly injected overdue-medical tracks. Penalties are 39→29 and 29→19, remain exactly two through completion, and no Council hearings occur. Festival finances remain £622.60 across those two injected death ticks, consistent with no fine. The day continues and closes; final reputation is 26, with the newspaper truthfully showing net 39→26, two deaths, 25 admissions and 23 surviving guests rated, without a celebratory quote. Death details identify causes and explain the −10/floor-zero/no-fine rule. The floor-at-zero boundary and other victim/cause variants rely on the already approved core tests; this EXE run itself did not reach zero reputation.

Survival independently exercises Management Only, All-Rounder manager/autopilot and first-person/manual switching, Steward Only tier-one/tier-two allocation and movement, separate saves, strict round trips and exact parked management restoration. The pre-existing rainy Survival top-bar overflow at forced 1280×720 remains visible, as documented in the prior UI review; this Classic scope does not repair that existing layout limitation or establish exhaustive Survival geometry acceptance.

Shared warning checks independently prove an injected real pre-replacement adapter failure preserves earlier manual-slot bytes, exposes X dismissal, leaves stage selection and accepted start/live ticks usable, stays dismissed after repeat failure, loads the older valid slot, and recovers the exact current session through pointer Retry. Screenshots show the compact retry state and its disappearance after recovery.

## Limits

These are deterministic scripted diagnostics using actual menu handlers/signals and normal paid setup commands; selected menu/X/Retry interactions use injected viewport pointer events. Death tracks are explicitly injected overdue-medical fixtures, not naturally observed fatalities. This is not broad manual player QA, natural-balance validation, exhaustive resizing, a performance assessment or a rerun of the entire source test suite. Mild/dry weather, half disorder pressure and doubled incident windows remain provisional rules supported by source/core review, not new balance findings. Stage expansion and other excluded features remain outside this acceptance.

Only this report and isolated independent-exe evidence were written by the reviewer. The copied delivery and pinned source remain unchanged; metadata-only handoff/status updates may follow this verdict.
