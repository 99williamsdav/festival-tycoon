# Booking table v2 — concept before implementation

User-authorized scoped revision. This concept supersedes the stacked-card booking layout only. Current Main.Booking.cs, Main.HudWorkspace.cs, FestivalProgramme.cs and LineupBooking.cs inspected. Native SVG concept artwork was produced before Builder graphical edits; no game code changed. Designer approves the layout for implementation and user review in game, no extra aesthetic gate.

## Exact geometry at supported sizes

Mockups: booking-1280.png (1280×720), booking-1920.png (1920×1080), booking-1280-filtered.png. All data rows use the actual six-band catalog. Selection/assignment is illustrative, not a saved run. Preserve the existing top HUD and bottom controls, not the simplified mockup text spacing.

Preparation panel x=16,y=72,width=viewportWidth−32,height=min(viewportHeight−138,660). Thus at1280×720 it is1248×582, bottom654 (top HUD ends58, bottom controls begin670). At1920×1080 it is1888×660, bottom732: cap height to retain some world view instead of a large empty cream area. Inner x36; heading baseline109; existing tabs baseline143. Programme only requires this enlarged layout; do not change unrelated panels or force live HUD to remain expanded.

At1280: table x36,width824; timetable x884,width360; gap24. At1920: table x36,width1160; timetable x1220,width664; gap24. Do not scale font down. At intermediate widths interpolate or allocate the extra width to Band/Professionalism/timetable, never below the1280 column minima.

Table title baseline184; genre selector y198,height34; table header y246,height48. Six rows y294 through546, **42px each**, all visible without ANY list/page/table scrolling or clipping at720height. No horizontal scroll, hidden columns or ellipsis. Header widths at1280, in order: **188,96,68,136,136,200**; total824. At1920:260,130,100,170,180,320; total1160. Names14px, full headers14px; full catalog names fit. Secondary labels12px, stars16px with18px pitch. Profession header fits200px. Full details do not depend on tooltips. Longer future names must wrap/expand design deliberately, not truncate the current six.

Timetable header “Main stage” and “Elapsed festival time · mm:ss”. Lane x=timelineX+54,y246,height300 (1px/second). Keep actual starts15/115/215seconds, ends90/190/290seconds;75px set blocks,25px changeovers,05:00close. Three-slot lane always visible. No new clocks, lengths or budget changes. Inline remove controls must be actual accessible buttons in runtime; mockup phrase is layout guidance. No narrow extra inspector.

Meaning row baseline570; selected/drop/status baseline592. Footer divider y=panelBottom−50 (604at720;682at1080), cost baseline=divider+25, readiness=divider+47, Start x=width−222,y=divider+10,size182×38. At720 the button ends652 inside panel654. Entire Programme root must honor this footprint despite existing container minimum-size propagation; check actual control bounds and disable inherited scroll if content fits. Bottom readout can wrap only within reserved space, not collide withStart.

## Table and controls

Columns exactly **Band, Genre, Price, Popularity, Ego, Professionalism**. Every header is a focusable clickable sort button; one active arrow/accessible sort state. Initial sort **Price ascending**, All genres filter. Price sorted by integer pennies, ratings by raw0–100, not rounded stars; Band/Genre by displayed text; ties stable by band name then stable ID. First activation of a new column ascending; repeat toggles descending. Tab/Enter/Space work. Sort/filter are cosmetic, not commands or changes to lineup/cash/PRNG/save.

Filter above table: All genres, Folk, Rock, Pop, Electronic. Display “6 of6bands” or filtered count and current sort. Retain full table headers; filtered rows stay top aligned without enlarging. Never filter timetable slots. Preserve selected ID if hidden and say “[Band] selected · hidden by genre filter”; no silent unassign/deselect. Assigned band remains in filtered table when its genre matches, marked “Assigned · Setn”; high-ego expectation is independent and must remain visible (a short second line or fullwidth selection line if assignment already occupies row subline). Sort must not change drag payload IDs/source slots. Avoid rebuilding during active drag; cancel drag safely if filter/sort changes.

Whole row supports existing actual drag from Main.Booking.cs as well as click/keyboard selection then slot activation. Selection fill pale teal, separate visible focus outline; assigned text marker independent of selection. Use the existing PreviewLineupEdit route for move/swap/replace/no-op/rejection. Valid slot hover teal “Place in Setn”; occupied external target amber “Replace [band]”; assigned-to-assigned “Swap”; changeover/outside/locked invalid, no mutation. Escape cancels; failed save restores prior valid plan. No new payment or booking button; cost/readiness/Start retain authoritative semantics.

## Ratings: exact display and semantics

Raw scores stay0–100. Compute `halfUnits = floor(clamp(score,0,100)/10 + 0.5)`, `stars=halfUnits/2.0`. Integer-safe equivalent for current integer scores `(score+5)/10` using integer division. Round midpoint UP (not C# default banker's rounding). Five outlined stars; fill whole stars and clip the next star's left half. Zero valid: five empty stars,0.0. Show numeric star total beside strip (e.g.2.5), not raw score in place of requested stars. Never use a solid star to represent half; use a native custom drawing path/mask or approved native glyph assets, no new raster suite needed.

Header sublabels always visible: Popularity “Audience appeal”; Ego “Demandingness” in muted brown #795336; Professionalism “Softens ego reaction”. Meaning line: “High ego = more demanding / headline-sensitive. Stars show intensity, not universal quality.” No all-green/gold review-rating treatment. Popularity must not acquire any new gameplay effect. All exact raw scores and meanings also available on hover AND keyboard focus, e.g. “Ego35/100 ·2stars ·Higher means more demanding/headline-sensitive”; “Professionalism65/100 ·3.5stars ·Softens ego reaction”. Accessibility name includes exact value and shown star count.

Current expected displayed triples Popularity/Ego/Professionalism: Meadow2/1/4; Barnstorm3/2/3.5; Orchard3.5/2.5/4.5; Field4/3/4; Copper4/4/3.5; Neon4.5/4.5/4.5. This rounds55→3,35→2,65→3.5,75→4,85→4.5; raw sorting remains55,35,etc.

**Exact user copy: “Expects to headline”. NEVER “Expects set3”.** Use wherever expectation is surfaced, including tooltip/status/drag hints. Current rule Ego>=70 triggers expectation (Copper/Neon); use authoritative predicate, not star rounding (65rounds3.5 but does not cross70). Expectation is not an invalid placement: non-headline slot remains allowed with existing reaction. Professionalism mitigates reaction, not expectation eligibility. Assigned status and expectation must both be accessible/visible when applicable.

## QA handoff

At1280×720 and1920×1080 assert six full rows and six full columns visible with no scroll, header bounds, stage three slots/time labels, both footer lines andStart inside panel. Screenshot Allgenres/Priceascending, alternate sort, Rock filter, selected+assigned, high-ego assignment expectation, actual rowdrag empty/replace/swap, keyboard assignment and live lock. Verify boundary rating0,5,15,65,95,100 and exact tooltip scores; rawties stable. Sort/filter/hover/cancel must preserve simulation hash/cash/lineup. Changing view size must not shift drop hitboxes away from slots. No gameplay/schedule/content/budget mutation from this design.
