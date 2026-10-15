# Classic copied Windows EXE independent review

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
