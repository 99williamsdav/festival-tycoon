# Classic booking UI independent review

15 October 2026. **Changes required: one bounded navigation defect remains.** This review covers the 18-file checkpoint in `reports/evidence/classic-tickets/ui/review-checkpoint.json` against the independently reviewed economic base `07a2cb99`, the booking brief and the user-approved designer handoff. All 18 source pins matched before execution and after the final native replay. No product or test source was edited by this reviewer.

## Navigation finding

**[P2] Route every Classic Programme entry through the paid booking screen.** The builder reported this during final review; independent source inspection confirms it. `game/Main.HudWorkspace.cs` creates an Overview “Choose acts” shortcut calling `SelectHudTab("Programme")` directly, while the interception exists only in `OpenPreparationDestination`. `SwitchToFestival` also selects the legacy Programme page. That legacy BookingPanel has no paid-edit preview callback: its SitePreparation branch invokes a null callback and returns, making post-sale edits inert if this page is exposed. Returning from the new screen can expose the underlying workspace, and manual load needs a deliberate paid-site landing state. Centralize the Classic route, close the workspace on return to site preparation, and add native coverage for direct-tab entry, Back and paid manual load. Re-review the bounded repair before accepting this UI slice.

## Verified behavior

The free draft uses authoritative capacity, price, demand guidance and conservative cash. There is no programme-spend cap; financial risk is advisory. Paid confirmation resolves once through the milestone host. Preview quotes replay the same journal as the command, retaining the original-announcement maximum impact and exposing additional refunds, released fees, replacement fees and resulting cash. Session identity plus authoritative hash guard acceptance; confirmation cannot duplicate the ticket sale. The native run verifies safe default contract focus and nonmutating Escape. Source restores originating-set focus after programme confirmation/cancel and clears stale dialogs on session switch/manual load.

Paid bands are excluded from Start cost and receipt totals. Accounts render ticket attempts with quantity, locked unit price and signed amounts; the native account view includes actual band releases. Forecast-first/free-draft return, manual draft load, cancellation, zero-sales completion, next-festival carry and actual negative-balance recovery passed. Unknown talent remains hidden in both the table and drawer; the normal Survival layout remains the default.

Both bounded core repairs are consistent with the intended contract: risk now warns below essential setup cost even within overdraft, without prohibiting confirmation; Classic settlement trims cached tap queue cells after departing workers are removed. The latter addresses the preserved native save-validation failure without changing the Survival array. The exact release/refill/cancel/zero-sales settlement and completed-save regressions pass.

Visually inspected all ten original PNGs in each of `native-approved-1280x720` and `native-approved-1920x1080`, alongside the three 720p concept images and HANDOFF.txt. The three-column layout, native ratings, receipt and compact consequences preserve the approved arrangement. Money, risk copy and actions fit both target sizes. Names use constrained cards/table cells with full details available through the existing selection/tooltip behavior. Independently generated 720p draft, consequence and risk images were also inspected.

## Independent execution

All lanes ran sequentially, with no export or reference re-recording:

- Rebuilt and ran the final focused booking/ledger/demand/terms/campaign/weather group: **56 passed, 0 failed, 0 skipped, 1m45.861s**, exit 0. Atomic save cases used authorized workspace TEMP/TMP. Log: `reviewer-focused-tests.txt`.
- Ran both original immutable Survival fixtures on those assemblies: **2 passed, 0 failed, 0 skipped, 3m45.087s**, exit 0. Log: `reviewer-immutable-parity.txt`. All ten original reference/fixture pins still match.
- Built `game/Festival.Game.csproj` with no restore: **zero warnings/errors**. Log: `reviewer-game-build.txt`.
- Ran the pinned Godot 4.7.2 console executable with the real booking capture at 1280×720, hidden window and isolated saves: **88 checks passed**, actual process exit 0, empty stderr, ten PNGs. Evidence: `reviewer-native/`, including `execution.json`, runtime, checks/journal and saves. Direct entry exercises the SpinBox LineEdit and Apply; click helpers push actual pointer input. This independently repeats the supplied native scenario, not a separate manual playthrough.

Paths above are relative to `reports/evidence/classic-tickets/ui/`. Final source/reference/assembly hashes are in `reviewer-verification.json`; original-reference checks also appear in `reviewer-survival-pins.json`. Earlier builder native logs were inspected: Classic 105, weather 244, Survival 63 and warning 24 checks with empty stderr. Those earlier runs preceded later compact-layout changes and are not represented as fresh reviewer executions.

No manual accessibility, universal balance or performance acceptance is claimed. This review does not accept a copied Windows EXE. Unrelated palette imports, lockfile edit, outfit source, legacy UID files and all immutable references were left untouched. Build/test/native lane released to the builder for the bounded navigation repair.

## Navigation repair re-review — first replay

All 18 navigation-repair checkpoint pins match; exactly five files differ from the initial checkpoint. Source inspection confirms central Classic Programme interception, explicit hiding of the underlying workspace on paid Back, and a farm/dock landing after successful paid manual load. Independent game build passes with zero warnings/errors. Independent `reviewer-navigation-native/` replay passes **92 checks**, actual exit 0 and empty stderr, including direct-tab routing, paid manual load and Back-to-site assertions.

The requested weather replay in `reviewer-navigation-weather/` exits **2** at `Main.WeatherEvidence.cs:81`: the old script attempts to find ReopenForecast after clicking the site menu while the newly routed Classic programme screen is still covering it. `ClassicReady(false)` uses commands to confirm, so it does not take the product receipt/close route. This is an outdated diagnostic navigation sequence, not an established product regression. The diagnostic must leave the programme through its actual Back control before opening the site menu, then rerun all weather/shared-mapping checks. The failure artifacts are retained. Acceptance remains pending that replay; lane released for the bounded diagnostic adjustment. Source pins after this run are in `reviewer-navigation-pins.json`.

## Final navigation and menu repair acceptance — 15 October 2026

**PASS for the final pinned source/UI slice.** This disposition supersedes the initial changes-required and pending-replay dispositions above; their findings and failures remain preserved. No remaining blocking finding was found in the reviewed scope. The accepted source is the 18-file `menu-repair-checkpoint.json`.

The corrected diagnostic Back route subsequently exposed a real menu stacking defect: the later readiness card covered the existing site menu, so its visible Weather forecast button could not receive the pointer. The builder retained the failed route/probe, including `open:false`, `same:true`, `menuVisible:true`. The final product repair moves the menu to the last sibling in its existing canvas parent whenever it opens. It does not change finance, persistence, simulation or modal layer numbers. The only other change since the navigation checkpoint is the weather diagnostic's actual Back pointer and pre-click menu screenshot. Exactly these two files changed; all 18 final pins matched before and after independent execution, recorded in `reviewer-menu-pins.json`.

Independently rebuilt the game with **zero warnings/errors** (`reviewer-menu-build.txt`), then ran pinned Godot 4.7.2 with the full weather capture at 1280×720 and isolated saves. **All 250 checks pass**, actual process exit **0**, empty stderr. The six added checks are actual Back pointer targets; all six forecast reopening records show open and unchanged authoritative state. Shared mapping now checks the real Classic programme screen's set timestamps, alongside forecast/live/results checks. Evidence is in `reviewer-menu-weather/`, including `execution.json`, checks/journal, screenshots and saves.

Visually inspected all six fresh menu-route originals: the menu and Weather forecast button now render above the readiness card. Also inspected the fresh mild/heavy forecast screens and the offset-mapping forecast/results originals. No clipping or blocking overlap was found in these repair captures. The independent 92-check booking replay, 56 focused tests and two immutable Survival fixtures recorded above remain the applicable earlier verification; core/test pins have not changed in these presentation repairs. No broad rerun or manual accessibility/balance/performance claim is added.

The source/UI slice may proceed to export and separate copied-payload review. This PASS does not accept a Windows EXE or its adjacent data. Build/test/native lane released.
