# Classic ticket phases — audit and implementation plan

15 October 2026. Implementation authorized; contract proposal approved by the user. Designer handoff pending. This document is a working implementation plan, not a new-build acceptance claim.

## Current financial facts

- Existing first-festival cash £450 already includes 25 fixed £10 tickets (£250) and the actual £200 starter loan. New Classic booking must begin on £200, with the same parked £200 liability; it must not give £450 plus new sales.
- Later Classic creation currently adds fixed £250 to carried cash. Remove that pre-credit only for the new Classic phase. Survival's opening and carry transactions remain byte-for-byte equivalent.
- PreparationPlan is currently entirely unpaid. CommitPreparationPlan pays act fees, hires, stock and Build together at opening. Splitting band settlement must change both ledger entries and validation; simply moving the UI button would double-charge bands.
- Opening is currently blocked below the existing minus-£100 overdraft. Booking confirmation must not reuse that setup blocker as a programme allowance. Actual negative cash after confirmation must have explicit rebooking/cancellation recovery.
- Accounts already expose ticket quantity/unit price, operating expenses, stock and cash reconciliation, but derive tickets from fixed tier values. Classic must read recorded sales/refunds and contract settlement; Survival keeps fixed-tier facts.

## Dependency audit

| Area | Current source | Required Classic boundary |
| --- | --- | --- |
| Opening money/ledger | CampaignPlanning.cs, CampaignSetup.cs | Loan or carried cash only before sales; ticket receipt and band payment as separate balanced facts |
| Draft edits/opening | PreparationPlan.cs, Preparation.cs, FestivalProgramme.cs | Explicit booking draft / site-preparation phases; already-paid act fees excluded from start charges |
| Audience identity/arrival | CampaignSetup.cs, Build setup, Person/medical/disorder/navigation state | Known capacity 25; only sold, unrefunded ticket holders become actual guests; never infer sales from later facilities |
| Demand/expectations | ActCatalogue.cs, LineupBooking.cs, SetPerformance.cs | Price, reputation and overlapping popularity draw only for demand; existing talent/live performance remains experience |
| Accounts/results | FestivalAccounts.cs, FestivalResults.cs | Actual units × price, refund quantities, released/replacement fees, exact net cash and expense reconciliation |
| History/carry | ClassicCampaign.cs, NextFestival.cs, BandRelationships.cs | Stop subtracting/adding fixed £250; historical actual sales, announcement and cancelled attempts must remain auditable |
| Strict save checks | Preparation.cs, PreparationPlan.cs, ClassicCampaign.cs, BandRelationships.cs, MedicalIncident.cs, DisorderIncident.cs, LivePerformance.cs | Replace Classic fixed-25/fixed-price assumptions with validated recorded sales; reject forged financial/roster state |
| Lifecycle/UI entry | Main.Classic.cs, Main.Forecast.cs, BookingPanel.cs, preparation controls | Forecast → booking → sales result/site prep; persisted phase, exact menu/load continuity and guarded start |

## Proposed core design

Add a Classic-only immutable booking/ticket snapshot, omitted from Survival JSON and hashes when absent. It records version, festival ordinal, cancellation attempt, phase, draft price, original announced lineup/popularity/fees, initial tickets, remaining tickets, monotonically highest disappointment, and ordered balanced financial events. The existing preparation snapshot continues to own actual current cash/holdings; do not create a second cash balance.

Demand is a pure calculation plus a separate stable local keyed market factor for campaign seed and festival ordinal. Replanning a cancelled attempt retains that factor. Reads/previews do not draw from live RNG. The public estimate exposes a range rather than the private exact factor. Popularity is combined with diminishing weights, not added as separate audiences; no genre-variety requirement or talent input. Capacity remains the inherited 25 and is shown before booking. Parameter calibration and example budgets will be recorded with tests.

Keep draft price and three act choices freely editable until confirmation, including unaffordable projected plans. Confirmation validates identities/eligibility/completeness, not a separate booking allowance. It records ticket receipts and each paid fee exactly once, then enters site preparation even if actual cash is negative. Site-prep start retains the existing solvency check for remaining unpaid setup. Offer rebook and cancellation recovery whenever that check cannot be met.

After sales, only an explicit rebooking command may change paid contracts. Compare against original announcement, retain maximum severity reached and refund only the difference from previous cumulative refund target. Never sell extra tickets on a change. Keep the paid contract fee immutable: release exactly what was paid, then pay the replacement quote once. No profit from repeating the same command or restoring an earlier dropdown selection. Preview money, ticket holders and disappointment before committing.

Classification proposal for the approved levels: reordering retained acts does not lose an act; changed acts are comparable when the replacement shares the original genre and is no more than five popularity points lower. A non-comparable removal of the original headliner or a popularity loss of at least fifteen points is substantial; other replacements are ordinary. Any changed name still incurs the approved comparable minimum, rather than zero disappointment. Preserve the greatest severity across all edits and use original announcement data, not current mutable popularity.

Cancellation before opening refunds every remaining ticket holder and every active unperformed contract, once; unpaid site draft is cleared. Record the cancellation and minus-five reputation consequence, return to booking on the same ordinal with the same weather/market, and retain actual campaign holdings. It must restore the pre-confirmation cash through real inverse cash events, not an arbitrary cash reset. Cancelled attempts do not generate played gigs, ratings, completion rewards or another advance-ticket credit.

Roster work is a material integration boundary: the existing factory creates 25 candidate guests and fixed staff/performer identities before booking. Actual sold guest selection must be centralized across person, medical, disorder, wallets, navigation and arrival schedules. Do not fake unsold people as deaths/departures or weaken validators. Preserve reserved candidate identities so refunds/cancellation cannot shift staff identities or generate new random markets. Audit candidate reconstruction before choosing the smallest safe implementation.

New save schema/Classic namespace must preserve the accepted v100 files intact unless an explicit verified migration is implemented. Survival namespaces and compatibility remain unchanged. Save validation must regenerate keyed demand and validate financial/contract event order, original announcements, refunds, cancellation attempts, current guest IDs, cash and carried histories. Save failures retain the already accepted nonblocking warning behavior.

## Slices and verification

1. Freeze current Survival references and run the original immutable fixtures. Add pure demand/estimate tests and numerical calibration examples without changing live sessions. Review the model and the audited integration plan.
2. Implement the Classic-only economic/phase snapshot, commands, settlement, recovery, roster and strict persistence. Test all phases, repeated/reloaded edits, high-spend confirmation, low/zero sales, negative balances, cancellation refunds, once-only reputation and exact balanced accounts. Run three-plus paid festivals with actual money/carry and genuine commands. Fresh Astra MEDIUM core review before UI acceptance.
3. Integrate the coordinator's approved Designer handoff through existing native controls. Preserve forecast order, save/menu/load/reset and ordinary resize. Add opt-in replay/native evidence. Fresh Astra MEDIUM source/UI review.
4. Export, copy to a new delivery folder, verify exact source/payload pins and run builder plus fresh independent actual copied-EXE acceptance. Preserve earlier builds and all immutable reference files. Record checks/commits/caveats; no broad balance/performance or manual-QA claim.

Required demand properties: price non-increasing sales for the same market; reputation/popularity non-decreasing draw; talent excluded; overlap bounded; coherent genre lineups viable; never exceed known capacity; seed/ordinal stable across edits/load; estimate encloses outcomes under bounded market factor. Required money properties: exact opening loan/carry, actual ticket credit once, band spend once, setup excludes already-paid bands, inverse contract releases, cumulative ticket refunds, full cancellation reverses actual sales/contracts, no extra ticket income, balanced ledgers and unchanged refresh/discount behavior.

## Isolated model checkpoint

The pure demand and approved contract calculations now exist in ClassicTicketDemand.cs and ClassicBookingTerms.cs, with no call from live session creation, booking, cash or UI. Nine focused tests passed (523 ms), including 65 actual starter markets at the existing lowest carry, price/popularity/reputation monotonicity, capacity, bounded estimates, live-talent separation, session hash stability and cumulative refund targets. The first compile used an obsolete MSTest assertion API and a numeric-literal type mismatch; these test compilation issues were repaired before the passing run.

Provisional demand parameters: overlapping popularity is 60% strongest, 25% second, 15% third (missing draft slots zero). Baseline reach 65%, plus 0.6 percentage points per popularity and 0.5 per reputation; price willingness anchor is £10 plus 8p per popularity and 6p per reputation. Price factor decreases linearly from twice the reach at free entry to zero at twice that anchor. Final quantity is floored and capped at known capacity. A local keyed market factor varies reach by at most 8 percentage points; public guidance spans plus/minus 10. These are starting balance parameters, not playtest acceptance.

An initial 55% baseline reach failed three of ten example lowest-cash recovery budgets. That evidence is retained as initial-calibration-reach-5500.json. Raising the provisional baseline to 65% gives the ten recorded examples 17–21 actual £10 ticket sales; their cheapest eligible bands cost £14–£33 and minimum required staff/facilities £128–£135. All ten can fund that minimum from the existing minus-£100 carry without exceeding the existing overdraft. The separate 65-market test passed. These are budget calculations, not actual played festivals or a proof for every seed; core integration must still demonstrate actual commands and recovery.

A first calibration rerun accidentally used the standalone probe's stale copied dependency through --no-build. Rebuilding that isolated probe against the current Simulation.dll corrected the displayed results; the saved final calibration is the rebuilt output. No product or financial values were changed to hide a test failure. Before implementation the immutable two Survival fixture tests passed all 63 state/hash/event/RNG boundaries in 3m19.450s. Reference pins are under reports/evidence/classic-tickets/before/.
