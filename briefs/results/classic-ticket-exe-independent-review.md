# Classic copied Windows EXE independent review

Current final disposition: **r2 copied payload PASS** (see final acceptance below). First-candidate rejection and repair history are retained.

15 October 2026. **CHANGES REQUIRED.** The first copied payload is not accepted: the Classic results newspaper still uses the old fixed ticket quantity, and Next festival promises the old fixed ticket quantity/price.

## Blocking findings

- **[P2] Use actual Classic ticket holders throughout results.** `game/Hud/ResultsPaper.cs:245` supplies `FestivalTickets.Sold(p.Tier)` to ReviewFacts for Classic as well as Survival. Independent actual copied-EXE screenshot `final/independent-exe-classic/04-first-results-next-festival.png` shows 19 punters but a 25-ticket poster and body describing 19 of 25 ticket holders. The actual live HUD is 19/19. This contradicts the resolved booking and the truthful-results requirement. Bind Classic review facts to its current paid booking, including zero sales and post-refund holders, while preserving Survival. Verify newspaper, poster and by-numbers tooltip, including zero-sales wording. The lineup uses actual performance records; no separate lineup defect is established.
- **[P2] Remove the fixed Classic next-festival sales promise.** `game/Hud/ResultsPaper.cs:113` unconditionally promises fixed guests and ticket price in the Next festival tooltip. Classic starts another free draft, with sales unresolved. Describe that accurately while preserving the existing carry explanation and Survival values.

The other fixed ticket references in PreparationDock are overwritten with Classic booking facts; the old BoxOffice route is excluded for Classic. No financial or simulation repair is requested.

## Executed payload and evidence

Exact executable: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-tickets-2026-10-15/FestivalTycoon.exe`; SHA256 `9B061F927073509DA429814BF3F85C1C91523B18E6097E5F6AEB9FD233D3F87D`. Adjacent data was used directly, with no --path, rebuild, export, reference recording or environment flags. Start-Process waited for actual exits; captures used isolated saves. Evidence paths below are relative to `reports/evidence/classic-tickets/`.

| Independent track | Requested size | Checks passed | PNGs | Actual exit / stderr bytes |
| --- | --- | ---: | ---: | --- |
| tickets | 1280x720 | 92 | 10 | 0 / 0 |
| Classic loop | 1280x720 | 105 | 9 | 0 / 0 |
| weather | 1920x1080 | 250 | 22 | 0 / 0 |
| Survival architecture | 1920x1080 | 63 | 8 | 0 / 0 |
| save warning | 1280x720 | 24 | 4 | 0 / 0 |

All five automated tracks pass; no runtime ERROR/WARNING/Exception appeared (SAVE_WARNING_NATIVE_PASS is a success marker). Their full original evidence remains in `final/independent-exe-*`, with actual executable/arguments/times/exits in each execution.json and compact `final/independent-exe-summary.json`. Passing automation does not override the visual defect.

Before and after execution, all 194 payload files, 2,037 source inputs and three prior delivery EXEs match the export checkpoint. The initial check also verifies all 18 final UI pins and the export log hash. Records: `final/independent-before-pins.json`, `final/independent-after-pins.json`. Payload count is 194 excluding README, 287,640,211 bytes per the size-checked checkpoint. The only committed difference from approved UI 6e10a61f to export source 2e41a627 is four one-line Godot UID additions, with no semantic change. The export log truthfully retains two missing-UID warnings and two tolerated texture-import f.is_null errors; these are separate from the clean runtime logs.

Visually inspected all ten ticket captures and all nine Classic loop captures, plus the first five weather originals and the three approved 720p concepts/HANDOFF. Booking, receipt, contract preview, cancellation, risk and recovery preserve the approved layout; no blocking clipping was found there. Full 53-image visual acceptance was stopped after the demonstrated results defect; remaining images remain preserved but are not claimed as visually reviewed. A repaired replacement payload requires renewed acceptance.

No product, test, reference or unrelated import/lock/outfit/UID file was changed by this reviewer. This is scripted native replay, not manual play, broad accessibility, balance or performance acceptance. Prior historical failures remain untouched. Build/test/native lane released for the bounded repair.

## Bounded results source/native repair review — 15 October 2026

**PASS for the repaired source/native scope only.** The first copied EXE above remains rejected and unchanged. This addendum authorizes proceeding to a replacement export and separate actual copied-payload review; it does not accept an EXE.

Independently inspected the four-file diff against 2e41a627 and verified all 19 `results-repair/checkpoint.json` pins before and after execution. ResultsPaper now derives Classic newspaper/poster/by-numbers holder counts from the authoritative booking view, retaining fixed Survival behavior. The Classic Next festival tooltip describes choosing acts and price before sales and retains its carry information. FestivalReview explicitly describes zero sales in both the standfirst and body. The remaining two files add native result assertions and two newspaper captures. No simulation, financial, persistence or demand behavior changes are present in this repair.

Independent `dotnet build game/Festival.Game.csproj --no-restore` completed with zero warnings/errors. The first sandboxed attempt failed without diagnostic errors; its original log is retained as `results-repair/independent-build.txt`. The authorized escalated retry succeeded, logged in `independent-build-escalated.txt`.

Independently executed pinned Godot 4.7.2 against the repaired source at 1280x720, with isolated saves: **tickets 100 checks PASS**, **Classic loop 109 checks PASS**. Both actual process exits are 0, stderr is empty and runtime logs contain no ERROR/WARNING/Exception. Evidence is retained under `results-repair/independent-native-tickets/` and `independent-native-classic/`, with execution metadata. Assertions verify the actual rendered poster/body and live control tooltips for first results, zero-sales results and recovered results, including absence of the false zero-sales standfirst. Visually inspected all three relevant original newspaper screenshots: first and recovered results show 19 tickets; zero-sales results show 0 tickets and truthful standfirst/body, with the established scroll layout intact.

All ten original immutable Survival fixture/reference pins remain unchanged, checked before and after; no broad fixture rerun or re-recording was performed for this presentation-only change. Verification is in `results-repair/independent-before.json` and `independent-after.json`. No reviewer source edits or export occurred. No manual-play, accessibility, balancing or performance claim is added. Build/test/native lane released for the replacement export.

## Final r2 copied Windows EXE acceptance — 15 October 2026

**PASS for the exact r2 copied executable and adjacent payload.** This is the current final delivery disposition. No remaining blocking finding was found in this scope. The rejected first candidate and its changes-required findings above remain preserved history; they are not retrospectively accepted.

Accepted executable: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-tickets-2026-10-15-r2/FestivalTycoon.exe`. SHA256: `76EFAE7AD4883B0648446554F8E5C39DDD4E5B08331A91B1BD4162B2424C422E`. Keep its adjacent `data_Festival.Game_windows_x86_64` directory. The 194-file executable payload totals 287,643,723 bytes, excluding README. Export source is independently reviewed repair commit `1dd89e921ce3f28f4f99884e522c44c3c3b96cfb`.

Independently ran all five tracks directly on that copied EXE and adjacent data, with no --path, rebuild, export or reference recording. Every actual process exited 0 with zero-byte stderr and no runtime ERROR/WARNING/Exception. The warning track's SAVE_WARNING_NATIVE_PASS marker is a success marker.

| Independent r2 track | Requested capture size | Passed checks | Original PNGs visually inspected |
| --- | --- | ---: | ---: |
| Booking/tickets | 1280x720 | 100 | 12 |
| Classic loop | 1280x720 | 109 | 9 |
| Weather/shared mapping | 1920x1080 | 250 | 22 |
| Survival architecture | 1920x1080 | 63 | 8 |
| Save warning | 1280x720 | 24 | 4 |
| Total | | **546** | **55** |

Execution metadata records exact executable, arguments, UTC times, actual exits and stderr size in each `final-r2/independent-exe-*/execution.json`. Full checks/journals/saves/logs and all original PNGs remain alongside them; compact results are `final-r2/independent-exe-summary.json`. Paths are relative to `reports/evidence/classic-tickets/`.

Visually inspected all 55 fresh original captures. The approved three-column booking arrangement, receipt and compact contract/cancellation dialogs remain legible; risk and negative-balance recovery expose their actual amounts. First/recovered newspapers now show 19 actual ticket holders, while the zero-sales paper shows 0 and explicitly states that no tickets sold. Native assertions also pass for the actual body and by-numbers/Next festival tooltips. All six weather/menu-route views show the menu above the readiness card; forecasts, live views, next forecast and offset mapping/results retain coherent labels. Survival first-person/management and warning dismissal/recovery captures render as expected. The pre-existing long Survival rain-HUD overflow at the right edge remains visible in management/resume captures; it is outside this Classic feature and no broad responsive-layout acceptance is claimed. No new blocking visual defect was found.

All 194 r2 payload files, 2,037 source inputs, 19 repair source pins and three prior delivery executables match before and after execution. Export log hash matches. The final preservation audit also verifies all ten immutable fixture/reference pins and all 194 rejected-first-candidate payload files unchanged. Evidence: `final-r2/independent-before-pins.json` and `independent-after-pins.json`. Existing palette-import/lock inputs remain part of the explicitly documented working-tree export. The export log retains two known unrelated missing-UID warnings and six tolerated texture-import f.is_null errors; clean runtime results above do not erase those export messages.

The source/UI reviews and their prior 56 focused tests plus two immutable fixtures remain the foundation; no redundant unit/fixture rerun was performed here. These are independent repetitions of the shipped scripted native diagnostics, including their explicitly diagnostic medical/death injections, not broad manual-play, accessibility, natural mortality, universal balancing or performance evidence. Historical bridge/diagnostic failures remain preserved. No product/source/reference edits were made during copied-payload review. Build/test/native lane released; final handoff status may now be updated to this r2 PASS.


## Final metadata acceptance — PASS (15 October 2026)

The final approved handoff, current design/index entries, appended decision, workspace task status, r2 README and superseded first README agree with the accepted r2 evidence: 546 checks, 55 inspected original captures, 194 payload files / 287,643,723 bytes excluding README, 2,037 source inputs, export source `1dd89e921ce3f28f4f99884e522c44c3c3b96cfb`, and EXE SHA256 `76EFAE7AD4883B0648446554F8E5C39DDD4E5B08331A91B1BD4162B2424C422E`. All 12 metadata checkpoint hashes matched before this addendum; handoff/review workspace mirrors and the r2 README mirror matched. The corrected design-index links resolve. The implementation report retains its chronological candidate history; current approval is stated by the final handoff and this review.

The documentation preserves the first candidate refusal, v101/older-save distinction, provisional balance, bounded automated coverage and existing Survival rain-HUD overflow. It makes no full manual, accessibility, performance or universal balance acceptance claim. A quick preservation check also confirmed all 19 repair source pins and the accepted copied EXE hash. No rebuild, export, native replay or product edit was performed for this metadata gate.

Evidence: `reports/evidence/classic-tickets/final-r2/independent-metadata-review.json`. This report's checked pre-addendum SHA256 was `B232CCFB51DC582B596ADDCD24CC9484CB93C9F9A1CE8B08EB7562413B79566E`; this addendum intentionally changes that one reviewed file. The coordinator may mirror the updated report and refresh its self-reference pins. Final r2 playtest disposition remains **PASS**.
