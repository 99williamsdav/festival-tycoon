# Festival Accounts — concept only

Deliverable: festival-accounts.png, a full document design board with sibling newspaper context. Generated using built-in imagegen; exact prompt in PROMPTS.md. Visually inspected: six sales rows, discounts, all expenditure rows, totals and cash reconciliation match the arithmetic below. No game files changed. Return for coordinator/user review before implementation.

## Design and navigation

Festival results offers Newspaper and Accounts as peer tabs, freely switchable with remembered scroll position. Accounts uses restrained ruled tables, aligned numeric columns, formal heading and cream paper. Newspaper retains editorial masthead, headline, rating, guest satisfaction and public events only. Remove its current accounts/profit/cash block entirely; don't merely hide detailed expenses while retaining profit. The illustrated newspaper photo, foliage emblems, slogan and quotation treatment are generated decoration, not new approved game assets, branding or recorded guest quotations. Prefer existing game art or omit decoration in production.

The right preview on the board explains the relationship; do not permanently squeeze both documents side by side in gameplay. One selected document occupies the results viewer. Keep tabs and Return to menu fixed outside document scrolling. Keyboard tab selection, clear focus, labelled controls, scroll keys and an accessible back-to-results/menu path must work without a forced second document visit. Preserve existing save-before-menu handling. Do not repurpose Esc into an unconfirmed destructive restart.

At 1280×720: use a roughly 1000px-wide document viewport with readable 16–18px body, 28–32px rows, fixed top navigation and vertical scrolling. Do not scale this entire board to fit720 or shrink the accounting text to match its overview. At1920×1080 a wider/taller viewport can retain two accounting columns; longer line items wrap without colliding with right-aligned amounts. Keep each table intact; at larger accessibility text stack the columns vertically. Repeat table headers when needed, expose section links (Income, Expenditure, Cash) and avoid horizontal scrolling. Totals can be a compact pinned summary only if they do not duplicate/conflict with document totals. The image is not a native-resolution layout test.

## Correct financial framing

Title Festival Accounts; subtitle Income and expenditure. This is an operating income statement plus cash reconciliation, NOT a literal balance sheet. The simulation does have inventory/equipment account types, but this commission does not invent a complete statement of financial position, depreciation, tax or financing mechanics. Opening funds and any financing movements must never be labelled sales revenue.

Group income by actual product and recorded rate, showing units and actual cash received. Full and50% rows prevent an incorrect quantity × full-price assertion. Current actual rates: chips £3/£1.50, soft drink £2/£1, beer £3/£1.50. Staff and performers receive50%; staff cannot buy beer, so discounted beer is band-only. Use actual recorded PricePennies, not reconstructed assumptions about every buyer. Whole pounds have no .00; retain real fractional amounts such as £1.50 and £0.60. Existing FestivalCurrency.Format provides this behavior. No ticket revenue is recorded by the inspected festival result sales projection; omit ticket/other-income lines unless supported by actual successful transactions in the relevant game path.

Operating expenditure groups individual act payments, individual paid hires, rental and facilities. Purchased sound equipment is capital cash expenditure under the existing ledger classification, not another operating expense; if present show separately in cash reconciliation. Free/retained staff are not invented paid hires. Maintenance/extra staff appear only when actually paid. Stock purchases are inventory/cash movements; expense only the recorded cost of sold/consumed stock. Never subtract both purchased stock and cost of items sold in operating profit.

## Source inspection (read-only, current checkout)

- src/Festival.Simulation/Immersion.cs: ImmersionPurchase records Id, AgentId, Product, Tick, PricePennies, CostPennies and ledger entries, one record per unit. Product enum is Chips/SoftDrink/Beer. These already supply product/rate quantities, revenue and cost-of-goods totals; no new sale counters are needed for current retained attempt data.
- src/Festival.Simulation/Preparation.cs: Payments carry OfferId, Attempt, amount and debit account. GetPreparationOffers resolves individual act/staff/rental/purchase labels. Equipment buy uses EquipmentAsset; other relevant contracts use AdministrationExpense. Filter by attempt.
- src/Festival.Simulation/FestivalProgramme.cs: inspected real act names/fees used in the illustration.
- src/Festival.Simulation/PreparationPlan.cs: committed plan stores purchased stock quantities. SetupPayments stores consolidated setup ledger entries, BuildCostPennies and stock quantities. It ALREADY includes underlying Payments and StockPurchase entries: aggregate either setup entries OR component records, never add both.
- src/Festival.Simulation/BuildLayout.cs: current catalogue fee and placement kind permit facility breakdown for the current immutable committed layout. Setup payment retains aggregate build cost, not per-item paid history. For durable historical detail or changing future fees, snapshot item kind/count/paid amount at commit rather than recomputing history from a later catalogue. Reconcile detail to recorded BuildCostPennies.
- src/Festival.Simulation/Finance.cs and FinanceFeedback.cs: ledger account definitions, owner filtering and currency formatting. Filter festival-owned entries; buyer cash/spending entries must not be counted as festival expenses.
- src/Festival.Simulation/FestivalResults.cs: FestivalResult currently stores aggregate results, not per-item financial lines. Revenue sums actual Purchases. ConsumedStockCosts includes legacy StockConsumed ×60p plus immersion purchase costs. Include any genuine legacy consumption separately if applicable; do not silently drop it.
- game/Main.Results.cs: current newspaper contains accounts and cash information in a second column, and already uses a scroll container and Return to menu. Concept proposes splitting this private block into a separate document, retaining public metrics and their definitions.
- Existing art reference: booking-newspaper-ui-v1/03-newspaper.png inspected. This commission supersedes its placement of profit within newspaper content, not its public rating semantics.

## Important data/reporting gap — no fix made

MakeFestivalResult currently derives contract/capital/cash totals from p.Payments and immersion.StockPurchase, not setup.BuildCostPennies. Build fees are charged and entered into SetupPayments as AdministrationExpense in CommitPreparationPlan but are absent from that result formula. Therefore blindly copying the current ProfitPennies/NetCashChangePennies into the new document would omit build expenditure for build-mode runs. Builder must reconcile the reporting projection with successful ledger movements when implementation is separately authorized; this design must not create a second contradictory authoritative result.

Current records are enough to derive most detail without new simulation counters, but FestivalResult lacks a frozen line-item report. Historical/load/retry-safe presentation needs either preserved attempt source records or a versioned frozen reporting snapshot; confirm lifecycle retention before implementation. Missing legacy data must say Not recorded, never a fabricated zero. Opening/closing cash should come from the relevant attempt's recorded boundary values with all owner cash movements reconciled; do not mix retries or campaign financing with sales. Existing loans elsewhere in the engine are not grounds for inventing loan rows in this example.

## Audited illustrative example

This is an arithmetic illustration, not captured play evidence or new balancing approval. Quantities/events are illustrative; inspected current catalogue rates used where shown.

- Chips:20×£3 +4×£1.50 =£66;24 units.
- Soft drink:20×£2 +5×£1 =£45;25 units.
- Beer:10×£3 +2×£1.50 =£33;12 units. Total61 units, income£144.
- Sold-item costs:24×£1 +25×£0.60 +12×£1 =£51.
- Acts:£40+£75+£55=£170. Sound hire£20; rig rental£30. Six facilities£20+£40+£80+£70+£50+£40=£300. Operating expenditure£520.
- Operating result:£144−£51−£520=−£427.
- Stock purchased:40 chips£40 +40 soft drinks£24 +32 beers£32 =£96. Unsold:16 chips£16 +15 soft drinks£9 +20 beers£20 =£45. £96−£45=£51 sold cost.
- Cash:£800 opening +£144 sales −£520 operating payments −£96 stock purchases =£328 closing; net cash change−£472. Difference from operating loss is£45 unsold stock. No capital purchase/financing movement in this example.
- Newspaper example:78% satisfaction maps to4stars under inspected thresholds,32guests,12completed guest beer servings,2fights,0medical collapses. These are independent illustrative public metrics; beers finished is not sales quantity. Matching the sample sales count is not an implementation identity and must never be used as one.

No runtime implementation, tests, source fixes or builder commission performed. Coordinator approval suffices under the user's standing agreement; no duplicate approval is required.
