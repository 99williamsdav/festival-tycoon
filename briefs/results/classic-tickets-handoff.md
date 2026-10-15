# Classic booking, ticket sales and site preparation — 15 October 2026

Status: APPROVED for playtest. Fresh independent economic/session, source/UI, bounded repair and actual r2 copied Windows EXE reviews passed. The superseded first candidate remains rejected and preserved. This entry supersedes the fixed Classic advance-ticket model in earlier dated reports. Stage expansion remains later.

## Accepted Windows build

`C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-tickets-2026-10-15-r2/FestivalTycoon.exe`

Keep `data_Festival.Game_windows_x86_64` beside the EXE. No ZIP. Prior Classic/weather/Survival folders remain intact. The executable payload is 194 files and 287,643,723 bytes; the adjacent README is excluded from this count. EXE SHA256: `76EFAE7AD4883B0648446554F8E5C39DDD4E5B08331A91B1BD4162B2424C422E`.

Economic/session source: `07a2cb99`. Reviewed native UI: `6e10a61f`. Godot metadata: `2e41a627`. Independently approved results repair/export source: `1dd89e921ce3f28f4f99884e522c44c3c3b96cfb`. All 194 payload files, 2,037 source inputs and the export log are pinned by `reports/evidence/classic-tickets/final-r2/export-checkpoint.json`. This is a working-tree export: existing 25 character palette imports and one lockfile edit were preserved and hashed, rather than represented as a clean-checkout build. The seven pre-existing unrelated UID files and outfit source remain untouched.

Export exited 0. It reported the known two missing unrelated UID cache-recreation warnings and six tolerated texture-import `f.is_null()` lines. Only the two newly generated unrelated UID files were removed afterward; the four ticket-owned metadata files were committed. The bounded results repair was separately reviewed before this export; no source semantics changed afterward.

## Player behavior

Choose Classic, see the keyed forecast, then choose the existing perk. Book all three acts and set a ticket price before confirmation. Draft changes are free. The approved three-column screen reuses the existing act table, ratings, detail drawer, fonts and running order, with actual capacity, demand range, causal guidance and conservative site-prep funds. Unknown talent remains hidden until earned experience reveals it. The UI warns when an otherwise valid booking leaves too little for essential setup; it permits the risk. There is no separate programme allowance or spend cap.

Confirmation locks the original announcement and ticket price, sells a stable capacity-capped quantity once, pays the actual band fees, and opens an exact sales receipt. Site funds are actual carried cash plus those ticket receipts minus paid bands. Site preparation and Start use the existing facilities, staff and stock costs, with no second band charge. Attendance uses the actual ticket holders; later facilities do not retrospectively create sales. Accounts record each sale/refund attempt as quantity × locked unit price, band releases, actual opening/closing cash and the existing staff/performer discounts.

Paid Programme remains editable through explicit previews. Released unperformed bands refund their paid fee; replacement fees are paid normally. Original-announcement comparable changes retain a maximum 5% ticket refund and −2 satisfaction points; ordinary changes 20%/−10; substantial changes 40%/−20. Refund quantities round up and only the incremental amount needed for the highest severity ever reached is refunded. Restoring/repeatedly changing the lineup does not reset disappointment or create a resale. Removing a band leaves an empty slot that must be filled before opening. Safe default focus and Escape retain the existing contract; acceptance returns focus to the originating set.

Full cancellation refunds remaining ticket holders and active unperformed band fees, reduces reputation by five with a floor of zero, clears the unpaid site plan and returns to booking the same festival. Weather, market key and festival ordinal stay fixed. The existing £100 overdraft remains; no loans, fines or new borrowing rules were added. A paid plan beyond the opening limit is saved as a recoverable state so the player can rebook or cancel.

The current Classic namespace is `r0-classic-v101`. Previous v100 and earlier files are preserved without migration. Survival/Steward preparation and fixed ticket behavior retain their existing rules. The accepted Classic loop, carried cash/debt/standing/acts/relationships/kit, seeded weather, shared time presentation and dismissible save warning continue. Every Classic Programme shortcut now opens this screen; paid Back/manual load return to site preparation. The open festival menu is raised above the readiness card so forecast reopening receives native pointer input.

## Evidence and independent review

The pure demand/terms and journal slices were independently approved at `546e132d` and `113f5b03`. The session review approved `07a2cb99` after repairing a legitimate completed stretch-act restore, using its original paid catalogue fee and historical standing. Two actual four-festival runs exercise carry, cancellation and paid settlement; a separate actual negative-balance route recovers from −£162.85 via cancellation to −£3.85 and paid completion. These are bounded cases, not a universal economy-balance claim.

The fresh UI reviewer independently passed 56 focused cases, both original immutable Survival fixtures (63 complete state/hash/ordered-event/RNG boundaries), a zero-warning game build, the 92-check booking replay, and the final 250-check weather replay with exit 0 and empty stderr. All 18 final source pins and all ten original fixture/reference pins matched. All ten original booking images at both 1280×720 and 1920×1080 were visually reviewed, plus fresh menu/forecast/mapping captures. The initial source review withheld approval for a paid Programme bypass; the repaired route, paid load and Back are independently covered. The first repaired weather route exposed the separate real menu/readiness overlap, now fixed. Original failures remain as history.

Two bounded integration repairs belong to the UI slice: the advisory shortfall threshold and Classic-only removal of stale water-queue cells after workers leave a zero-sales festival. The latter repairs strict completed-save validation. Survival keeps its original queue array, and the immutable fixtures passed without re-recording, exclusions or repinning.

The first copied candidate passed all five original automated tracks but independent visual review withheld acceptance: the Classic review facts still supplied 25 tickets and Next festival promised the old fixed quantity/price. Those findings and the original payload/evidence remain preserved under `final/` and the superseded delivery folder. The poster lineup was already based on actual performance records; it was not a separate defect. Repair `1dd89e92` uses actual remaining ticket holders throughout the paper, accurate zero-sales standfirst/body text and free-next-booking wording. The fresh independent narrow repair review rebuilt without warnings, passed the final 100-booking and 109-Classic native tracks, inspected first/zero/recovered newspaper originals, and verified all 19 final source and ten immutable pins. That source approval permitted this r2 export; it did not accept either EXE.
Builder replayed all five tracks against the actual copied EXE, using hidden Start-Process windows with Wait/PassThru, no game source path and isolated saves:

| Track | Checks | Failures | Exit | PNGs |
| --- | ---: | ---: | ---: | ---: |
| Booking, sales and contracts (1920×1080) | 100 | 0 | 0 | 12 |
| Classic progression (1280×720) | 109 | 0 | 0 | 9 |
| Weather, forecast and shared time (1280×720) | 250 | 0 | 0 | 22 |
| Survival/Steward (1920×1080) | 63 | 0 | 0 | 8 |
| Save warning (1280×720) | 24 | 0 | 0 | 4 |

All five builder stderr files are empty; runtime logs contain no ERROR, WARNING or Exception entries. All 194 payload, 2,037 source and three older-delivery EXE pins match after execution. Builder inspected the original copied-build estimate/receipt captures and the repaired zero-sales newspaper. Fresh independent copied-payload acceptance PASS: all five tracks independently passed the same 100 / 109 / 250 / 63 / 24 checks (546 total), with actual exit 0, empty stderr and no runtime errors. The reviewer used 720p booking/Classic/warning and 1080p weather/Survival, inspected all 55 fresh original captures, and verified all 194 payload, 2,037 source, 19 repair and three older-delivery EXE pins before/after. The export log, ten immutable pins and all 194 rejected-first-candidate payload files also match. No blocking findings remain in this scope. See classic-ticket-exe-independent-review.md; the original refusal and repaired-source approval remain in its history.

## Limits and related work

These are provisional approved demand and contract parameters. Evidence uses deterministic known seeds and real paid commands; the existing explicitly labelled death/medical injections remain diagnostic-only. No full-suite, broad manual accessibility, natural mortality, balance or performance claim is made. The pre-existing long Survival rain-HUD overflow at the right edge remains visible in management/resume captures; no broad responsive-layout acceptance is claimed. The prior weather delivery's isolated Godot bridge exception and same-byte passing repeat remain documented in its own handoff; this report does not erase that history.

The separately approved coffee van is committed and parked at `a8f6f573`, outside the game's runtime resource root in `assets/source/environment/coffee-van-v2/`. Its machine-side collision, future short-reach/cosmetic-cup direction and unoptimized geometry remain documented. It is not integrated or included as a coffee feature in this EXE. Stage unlocks, new stage prices, movable stages and further weather systems remain outside this delivery.

Owning brief: `briefs/classic-booking-ticket-phases.md`. Reviews: `classic-ticket-session-independent-review.md`, `classic-ticket-ui-independent-review.md`, and the separate actual copied-EXE review. Final repaired payload and native evidence live under `reports/evidence/classic-tickets/final-r2/`. Superseded first-candidate evidence remains under `final/`, and source repair evidence under `results-repair/`.





