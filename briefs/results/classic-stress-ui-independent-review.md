# Classic stress game/UI independent review

Reviewer: fresh independent GPT-6 Astra, medium reasoning. Date: 15 October 2026.

## Scope and disposition

**Source review cleared after one bounded repair; native/evidence acceptance pending.** Reviewed the current uncommitted game/UI changes relative to `d54224f7`, including `Main.ClassicDevelopment.cs`, `Main.ClassicStressEvidence.cs`, modified HUD files and their existing host/audio/presentation consumers. Read the architecture review and accepted simulation-core review. Construction and simulation-core acceptance remain separate.

The builder owns the exclusive build/test/native/export lane. This reviewer ran no tests, imports, builds, native application or exports and changed no implementation or fixtures. This report is not permission for playtest handoff or export acceptance.

## Finding and repair

**P3 — Restore local set focus after a non-primary paid-booking dialog.** `game/Main.ClassicTickets.cs` passed the global contract slot (3–8 for later stages) to `ClassicBookingScreen.FocusSet`, which resolves only `ClassicSet1` through `ClassicSet3`. Cancelling a Barn/Pond edit therefore failed to restore the requested keyboard target. The builder changed both confirmation and cancellation calls to `slot % 3`; `AcceptedEdit` also uses the local slot. Independently inspected the repaired source. The harness now cancels the Pond headliner dialog, checks focus on `ClassicSet3`, and reopens the preview before applying the removal. Actual final native focus evidence remains pending.

## Source assessment

- Development entry is explicit under development tiers, with all seven nonempty stage subsets selectable and both diagnostic toggles initially off. The empty selection disables Start; the separately labelled defaults action selects all three stages. The picker distinguishes synthetic reputation 40 from recorded two-star/14-earned-reputation history and £261.90 carry.
- A separate cached development host and `r0-classic-development-v1` directory are selected from persisted diagnostic identity. Ordinary continuation filters out development snapshots. Host parking preserves the ordinary Classic host when moving between modes; results continuation retains the active host.
- Paid edits expand the selected stage's three local slots into the complete contract array, pass the global index to preview/accounting, and now convert back to local focus correctly. The stage picker clears selected acts. Existing cross-stage duplicate protection remains active.
- Primary-active generic HUD calls are appropriate for Barn/Pond at index zero. Trailer set meshes explicitly request physical Trailer identity. Existing stage audio, venue labels and performer consumers use physical stage IDs. Farm peak draw tests Trailer presence; pooled supply enumerates actual own generators.
- Staff slots cover all three active stages and resolve the matching physical title. The `staff` offer category is sound-only (medic/steward use separate categories), so its use in the slot finder does not confuse other crew with the primary engineer.
- The test preset uses production commands for nine paid contracts, three engineers and site/stock plans, with site charges due at Start. Source does not claim guarantee-100 or top-up are ordinary behavior. Persistent test labels appear in forecast, bookings, top bar and results.
- The native harness uses production commands and snapshots, hashes ordinary save files, checks slot-nine edits/refunds and separate hosts, exercises Barn/Pond own-generator cutoff at index zero, and records actual admitted/departed totals and result records. Its clip explicitly states scripted fast-forward timing. Profile output discloses omitted input/hover work and its warmup/camera limitations. These are suitable evidence mechanisms, but their assertions and final artifacts still require successful execution and inspection.

## Evidence boundary

`reports/evidence/classic-stress/builder-native` is superseded. Builder-reported expanded run `builder-native-final` stopped on an evidence-slot filename containing a period; the harness now replaces periods with hyphens. `builder-native-final2` was compiled before the local-focus repair, so it cannot establish that repair. Final repaired native run, screenshots/clip, normal-mode regression, profile results and any eventual exported artifact require review before acceptance. No source-level blocker remains from this pass.

## Native screenshot review — builder-native-final2

Independently viewed original 1280×720 PNGs 01–08, 11–19 and clip frames 0010, 0140, 0280. This was file inspection only; the builder retains the execution lane. The assembly predates only the focus repair.

**One new P2 layout finding remains open:** in 15–19, the expanded three-stage opening checklist reaches the floating preparation camera controls. Rotate/zoom controls obscure the generator-over-capacity warning at the lower right (approximately y=574). Keep these controls clear of the dynamically sized checklist, or hide them while it is shown, then capture the repaired three-stage checklist. The warning must remain readable before Start.

**Evidence correction:** `01-development-menu.png` contains the ordinary Festival 1 forecast rather than the development menu. Close the foreground forecast before capturing that menu. This image does not establish visible menu navigation.

The inspected portions otherwise pass visual review: diagnostic toggles start visibly off; provenance/defaults copy fits; three forecast programme rows and three required engineer cards are visible; Trailer/Barn/Pond booking choices show their own acts and timings; the Pond release dialog names Set 3/headline and shows the £400/40-ticket refund; its post-edit screen displays 60 holders and an empty Pond headline slot. Barn-only and Pond-only views identify their physical venues and generators. Trader and bin second pages show usable navigation and appropriate entries. Extra-post labels explicitly say unstaffed. Live frames display all three physical stages with the test identity and clear scripted 100 + 27 + 6 = 133 caption.

These screenshots do not independently prove generator cutoff behavior, audio, every stage subset, all-person completion or save restoration; those need the supplied run checks/core evidence. Final copied-EXE focus proof, repaired checklist/menu captures, completed run evidence and export verification remain pending. The earlier source-cleared disposition is therefore superseded by **visible portions accepted except one bounded layout repair; final acceptance pending**.

## Bounded repair re-review and completed native checks

Independently inspected the new `Main.HudWorkspace.cs` repair: every refresh restores the default camera-control location and moves it left of visible readiness content with a gutter when their global rectangles intersect. This resolves the identified layout cause in source; the new frame-184 nonintersection check and final image still need execution. Removing the obsolete `/50` expected-people denominator is appropriate. The diagnostic-only forecast closure now occurs after the isolation save (which can refresh/reopen the forecast), correctly preparing the menu capture. No new implementation finding.

Independently read completed `builder-native-final2/checks.json`: all 58 entries passed, including normal-save/host isolation, contract/refund/cancellation roundtrips, Barn/Pond physical cover and own-generator cutoff at active index zero, additional-post/removal state, 133-person paid-site start, all three stages live, full admission/departure completion, nine-set results and exact next cash carry without another grant. These are builder-run checks, not reviewer execution.

Viewed `09-hundred-attendee-results.png`: readable development-test identity, 100 punters/nine sets, three-star result and reputation 40→47. **Evidence-only correction still needed:** `10-next-test-festival.png` captures that same results screen because it is saved in the same frame as `BeginClassicCampaign(next)`. Delay its capture to a later rendered frame before quitting, so it shows the next festival rather than the previous framebuffer. Next-state correctness is already supported by the passing roundtrip/carry assertions; the screenshot currently does not show it.

Disposition: no open implementation-source findings; source repairs cleared. Final copied-EXE focus/nonintersection checks, corrected menu/next screenshots and export verification remain pending.

## Final harness source re-review

Inspected the frozen harness corrections. Frame 677 now presses the actual `NextFestival` UI button, checks edition 3 and exact cash carry, and saves that state. Frame 685 checks an open forecast with the results paper closed and captures the next screen after rendering has had time to update. This addresses the stale-frame evidence defect and strengthens the continuation check.

The profile now measures 90 wall seconds after its warmup, records accumulated engine delta separately, and reports viewport render CPU/GPU counters alongside script simulation/presentation/autosave scheduling times. The retained failed 45-second baseline is correctly superseded rather than relabelled: insufficient engine elapsed time cannot establish a 30-second autosave cadence. The final assertion still requires a successfully restored cadence save after tick 8000, so the longer window does not silently waive persistence verification. Hardware/engine counter availability and performance quality must be judged from actual profile output; source acceptance does not imply a frame-rate target was met.

**Final source disposition: ACCEPTED for the game/UI and diagnostic harness slice, with no open source findings.** Previously inspected native portions remain accepted within their stated scope. Final copied-EXE focus/nonintersection/next-forecast checks, corrected menu/checklist/next captures, profile evidence and export verification remain the final acceptance gate. No reviewer execution or additional source changes were performed.
