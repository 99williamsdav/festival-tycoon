# Classic weather copied Windows EXE — independent acceptance

15 October 2026. Fresh independent GPT-6 Astra reviewer, medium reasoning, under docs/WORKING-AGREEMENTS.md. Scope: actual copied Windows payload from core 9208ad8879b7e8637988e7f627ed95a140da4a6b and UI 9f24cbeb8696418414063d1085be3316f5283bd7. Read the owning core/UI approvals, plan, time audit and final handoff. No builds, exports or product-source modifications were performed.

## Verdict

PASS for the commissioned copied-EXE acceptance, with the explicit nonblocking intermittent bridge-log observation below. No actionable feature or regression finding remains. This is not an assertion that every execution was error-free.

Delivery: C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-weather-2026-10-15/FestivalTycoon.exe with adjacent data_Festival.Game_windows_x86_64. EXE SHA-256: F1DD73A79BBE81F395B509D37D5C7C31BE3DF79376442C4D5BC54B5A4165526F.

Independently verified all 194 payload pins (287,487,871 bytes), all 2,022 source pins and the export-log hash before execution and after all five runs. Zero mismatches. Preserved pre-existing character-import/package-lock dirt is outside this review. Previous deliveries and player saves were not modified by this reviewer. Diagnostics use isolated output/save directories.

## Independent execution

After explicit native-lane release, launched the actual delivery with Start-Process -WindowStyle Hidden -Wait -PassThru and recorded actual process ExitCode. All runs were sequential. Weather used --fixed-fps 30 --quit-after 500 with ordinary 1920x1080 output, no capture-size override. Other tracks used --quit-after 260 and --capture-size 1280x720. No start-case shortcut was used.

| Directory under reports/evidence/classic-weather/final/ | Passing checks | Failed checks | Actual exit | PNGs |
| --- | ---: | ---: | ---: | ---: |
| independent-exe-weather | 243 | 0 | 0 | 16 |
| independent-exe-classic | 101 | 0 | 0 | 9 |
| independent-exe-survival | 63 | 0 | 0 | 8 |
| independent-exe-warning | 24 | 0 | 0 | 4 |
| independent-exe-weather-repeat | 243 | 0 | 0 | 16 |

Weather evidence.json and Survival architecture-evidence.json explicitly identify the copied delivery executable runtime path. Classic and warning checks.json contain the respective passing arrays. The original four tracks produced 37 PNGs; all 37 were independently visually inspected. The repeat produced 16 additional PNGs and was used to investigate log recurrence; no claim of separate full visual inspection of those additional images is made.

All six forecast conditions show readable native text, five temperature samples, actual programme/set gaps, closing, separate categorical rainfall, initial Continue focus and unobstructed footer controls. Showery displays 02:46–03:10; HeavyRain displays all seven Heavy intervals and Downpour 06:38–06:45 without clipping. Captured wet-label bounds fit, and journal rain intervals agree with displayed elapsed timestamps. Back, Continue, preparation-menu reopening, unchanged authoritative hashes, strict draft/manual/live save roundtrips, wet-day stock/matting and normal paid start all pass. A natural next festival reopens with keyed ordinal data.

Live chip temperatures/rain and existing categorical heat checks pass for each condition. Heatwave live image contains no residual rain after Showery; wet fixtures show existing rain/ponchos. All six Box Office summary/advice assertions pass. Ordinary Showery newspaper wording passes the native label assertion; the screenshot shows the top newspaper frame, so full body wording is not claimed from the screenshot alone.

Temporary 60-minute mapping checks pass for forecast, programme/gates, live clock, world NEXT, actual Performances and actual casualty newspaper text while duration remains 08:00 and authoritative state is unchanged. Performances screenshot visibly shows 61:00–62:45, 63:25–65:10 and 65:50–67:50. Mapping is restored; subsequent forecasts show 00:00–08:00. The extra overdue-medical injection is separately labelled in the journal and is diagnostic-only. Native casualty paragraph assertions, rather than a dedicated scrolled screenshot, verify the complete mapped body text.

Classic progression through four festivals, live/menu/save continuity, recorded nonterminal deaths and results pass. Survival management, all-rounder, first-person, Steward tiers and management resume pass. Save-warning dismissal, continuing play and successful recovery pass; screenshots show each state. Existing rainy Survival forced-1280 top-bar overflow remains visible and is the acknowledged pre-existing caveat, outside this weather acceptance.

## Retained log observation

The first weather run recorded one System.InvalidOperationException: Handle is not initialized in Godot.Bridge.ScriptManagerBridge.SetGodotObjectPtr. The same event appears in runtime.log:26 and stderr.txt:1; stderr is 496 bytes. There is no product callsite in its stack. File timestamp correlation suggests the mapped Performances/results-to-Pleasant-session transition, but does not establish exact execution phase or cause. The run still completed all 243 assertions, 16 captures and exit 0; no visible defect was found.

A separate full same-byte weather rerun completed 243/243, exit 0, 16 PNGs, empty stderr and zero ERROR:/WARNING: log entries. Classic, Survival and warning also have empty stderr and no such log entries. The original error evidence is retained and not replaced by the repeat. On this evidence the isolated observation is nonblocking for the scoped playtest acceptance; cause remains unconfirmed and future absence is not established. No speculative source workaround or error suppression was introduced.

## Evidence and limits

Machine-readable summaries: independent-exe-weather/run-summary.json and final-pin-verification.json. Each track retains runtime/stdout/stderr logs, actual exit-code.txt, structured checks, isolated saves and PNGs. Entry pin verification is retained in the review tool transcript; post-run verification is saved on disk.

Known-seed six-condition fixtures establish deterministic behavior through paid preparation, not a probability sample or natural balance claim. Injected death fixtures do not establish natural mortality. No broad manual QA, performance, exhaustive accessibility or screen-reader certification is claimed. Default elapsed presentation remains unchanged; this acceptance introduces no real opening clock, day-length, simulation, schedule, stage, unlock, pricing, night or new-hazard scope.

Final handoff was factually read and its validation boundaries agree with this review. Final status/routing metadata may replace pending acceptance wording with this PASS and must retain the bridge-log caveat and the distinction between builder clean logs and the first independent weather run.
