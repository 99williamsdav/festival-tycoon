# Classic weather and forecast — Windows handoff, 15 October 2026

Status: APPROVED for playtest. Fresh independent core, source/UI and actual copied Windows EXE reviews passed. The previous Classic and Survival deliveries remain intact.

## Delivered build

C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-weather-2026-10-15/FestivalTycoon.exe

Keep data_Festival.Game_windows_x86_64 beside the EXE. The earlier Classic and Survival deliveries are preserved. No ZIP was created. Copied payload: 194 files, 287,487,871 bytes. EXE SHA256: F1DD73A79BBE81F395B509D37D5C7C31BE3DF79376442C4D5BC54B5A4165526F.

Core source: 9208ad8879b7e8637988e7f627ed95a140da4a6b. UI/export source: 9f24cbeb. Export completed with exit 0. The final checkpoint pins all 194 payload files, 2,022 source inputs and the export log. Existing unrelated 25 character imports and one package-lock deletion were preserved and recorded; this is not a clean-checkout claim. Only the executable and matching data folder were copied from the export root. The adjacent README describes entry, saves and provisional behavior.

## Player behavior and scope

Classic draws a seeded forecast for each festival ordinal before preparation. The user-approved provisional weights are 35% Mild, 25% Pleasant, 20% Cloudy, 15% Showery, 3% Heatwave and 2% HeavyRain. The separate keyed draw does not consume gameplay RNG. Persisted condition, five authoritative Celsius samples and exact rain intervals are validated against campaign seed and ordinal. Menu, reopen and load cannot reroll them.

The approved forecast design uses its exact supplied mosaic. Native runtime text and geometry show arrival, the actual three-set programme and gaps, temperature samples and curve, categorical rain and every exact wet window. Continue enters the existing preparation/perk flow; back returns to menu. The preparation menu can reopen the same forecast. The live chip displays interpolated Celsius with existing weather icons.

Ordinary showers reuse existing rain, wetness, cover, stock and visuals at the bounded lower intensity. Extreme days reuse existing heatwave/heavy-rain behavior. Celsius uses integer interpolation between saved samples. Physiology retains existing categorical hot/non-hot behavior and gains no new continuous temperature factor. Weather is accurate, without forecast uncertainty.

All applicable player-visible festival timestamp consumers use one shared presentation source: forecast, booking, clocks, stage/band, status/evidence and results. Default output remains elapsed placeholder time. No real opening clock, schedule, day length or set duration changes. Durations/countdowns and save/system dates remain separate. A temporary 60-minute offset is used only in labelled diagnostic checks and restored afterward. Saved causal evidence remains authoritative; its explicit tick references are mapped at the UI boundary. The newspaper now renders all causal review paragraphs inside its existing scroll.

Classic uses r0-classic-v100. Earlier fixed-mild v99 files are preserved without migration. Survival namespaces and compatibility remain v99. Existing Classic carry, half disorder growth, doubled incident windows and once-only minus-ten reputation per death remain. The shared dismissible save warning remains available.

No stage expansion, unlock thresholds, prices, loans, death fines, wind, lightning, snow or night are added. The separate future stage/rental-placement decision remains LATER.

## Validation and independent reviews

Core checkpoint 9208ad88 has fresh independent Astra MEDIUM approval: 11 weather cases and 144 additional isolated checks across all six conditions passed. Builder's earlier 41-case group precedes the final ground-cover capability change. The final 13-case group includes the 11 weather cases and two immutable Survival parity fixtures covering 63 complete state/hash/ordered-event/RNG boundaries. These groups overlap; no full-suite claim is made. Original Survival fixtures and parity tests remain unchanged from baseline 0055f741, without exclusions or repinning. Final audit diff exit is 0.

Fresh independent source/UI review passed the final full 243-check native weather diagnostic, exit 0, with all 16 PNGs inspected and all 42 merged pins unchanged. No outstanding findings remain. Builder also passed 243 checks with 16 PNGs and the existing source regressions: Classic 101, Survival 63 and save warning 24, each exit 0.

Builder then ran the actual copied EXE, using Start-Process -Wait -PassThru and isolated output/save directories:

| Track | Passing checks | Failures | Exit | PNGs |
| --- | ---: | ---: | ---: | ---: |
| Weather/forecast/shared time | 243 | 0 | 0 | 16 |
| Classic loop | 101 | 0 | 0 | 9 |
| Survival | 63 | 0 | 0 | 8 |
| Save warning | 24 | 0 | 0 | 4 |

Builder runtime/stderr logs have no error or warning entries. Weather and Survival evidence identify the actual copied executable runtime path. All 194 payload and 2,022 source pins match after execution. Previous Classic and Survival EXE hashes remain unchanged. No player save directory was reset or migrated.

Fresh independent Astra MEDIUM copied-EXE acceptance passed all four tracks: weather 243/243, Classic 101/101, Survival 63/63 and save warning 24/24, each exit 0. The reviewer used ordinary 1920x1080 stretch for weather, inspected all 37 PNGs, verified the actual executable path and matched all 194 payload and 2,022 source pins before/after. No actionable findings remain in this scope. See classic-weather-exe-independent-review.md.

## Limits

The first independent copied weather run passed all 243 assertions and exited 0, but runtime.log and stderr.txt recorded the same single System.InvalidOperationException ("Handle is not initialized") in Godot.Bridge.ScriptManagerBridge.SetGodotObjectPtr near the mapped-results to next-session transition (inferred from file timestamps). It provides no game-code callsite. The reviewer retained that run and repeated the full weather track against the same copied bytes: 243/243, exit 0, 16 PNGs, empty stderr and no error/warning entries. No assertion failure or visible defect was observed. Independent review treats this as a nonblocking intermittent bridge-log observation; its cause remains unconfirmed. This delivery does not claim every run was error-free. Both first-run and repeat evidence are retained.


Weather diagnostics visit six known-seed conditions through actual Classic creation, normal paid preparation, wet-day stock/matting, strict draft/manual/live resume, pointer back/Continue/menu/reopen and actual next-festival flow. They establish deterministic paths, not the natural frequency or balance of six successive random days.

One separately labelled overdue-medical injection verifies mapped causal newspaper text in the temporary mapping diagnostic. The older Classic death diagnostic also labels its explicit injections; neither edits cash/reputation/results. These are not evidence about natural weather mortality. Mapped Performances ranges are visible in the final screenshot; the complete mapped casualty paragraph is verified by the actual native label assertion and can require normal newspaper scrolling.

The pre-existing rainy Survival top-bar limitation at forced 1280x720 remains outside this feature's layout acceptance. No broad manual QA, natural-balance or performance claim is made. No OS screen-reader session was performed.

## Evidence

- Plan and authoritative data contract: classic-weather-plan.md.
- Core approval: classic-weather-core-independent-review.md.
- Native implementation and time consumer audit: classic-weather-ui-implementation.md and classic-weather-time-audit.md.
- Source/UI approval: classic-weather-ui-independent-review.md.
- Copied-EXE review: classic-weather-exe-independent-review.md.
- Immutable references: reports/evidence/classic-loop/survival-before/ and survival-before-checkpoint.json.
- Final source UI evidence: reports/evidence/classic-weather/ui/native-complete/ and review-final-repair/.
- Payload/export/source pins and copied execution evidence: reports/evidence/classic-weather/final/.
