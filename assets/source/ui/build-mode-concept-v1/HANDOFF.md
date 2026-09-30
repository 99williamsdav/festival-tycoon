# Build mode — design proposal v1

Status: concept-only, for coordinator/user review. No game code, scenes, balance values or 3D assets changed. Coordinator approval counts as user approval under WORKING-AGREEMENTS.md; no duplicate approval request is needed.

## Deliverables and provenance

- `build-mode-design-board.png`: primary fresh-build catalogue, placement detail and compact retry/defaults treatments.
- `PROMPTS.md`: complete generation prompt. Built-in image generation, inspected visually for text and layout.
- References inspected: approved `hud-preparation-review-v2/preparation-overview-1280.png` and native `C:/Projects/festival-tycoon/reports/evidence/R0.05s/reviewed-native-1920x1080/01-owned-free-default.png`.

This is a generated UI design board, not native screenshot evidence. The world, stage and thumbnails are illustrative placeholders; they do not commission replacement buildings, a new stage, landscaping or service models. Production must use the approved assets and actual map. The board carries the approved dark edge-to-edge top bar, cream workspace, restrained teal actions and serif headings; its extra environmental detail is not an art-direction change.

## Layout and discovery

Build is a preparation destination alongside Overview, Programme, Staff, Equipment and Stock, available throughout prep. This is not a sequence or irreversible wizard. Existing Site & water controls must remain reachable: proposed mapping is service placement under Build and non-placement water management under the existing site/water destination, not deletion of that functionality. Exact tab arrangement remains a review choice.

Open catalogue is a compact left drawer; all six services have an icon, explicit name, unit fee and placed count. At 1920 target roughly 360–400 px; at 1280 target roughly 300–320 px with single-line compact rows. Collapse the drawer when placing. Keep the game field usable and never require a wide modal to position an object. The board is a composition proposal, not verified pixel specifications at either resolution; native 1280×720 and 1920×1080 checks are required during implementation.

Fresh festival: all six movable service types begin unplaced. Only fixed stage, generator and farm buildings remain. Taps and toilets support multiple placements; the other four allow one each. Once a singleton is placed, replace + Place with a Locate/Move action and show 1 / 1. For multiples retain + Place and the actual count. Selecting an existing service provides Move and Remove from draft. Keyboard focus/tooltips must expose the same names, prices, counts and actions as mouse use.

## Draft accounting — not balance approval

Every number on the board is illustrative/TBD, including £800 budget and the six prices. These are not approved configuration values. The fresh example shows £0 draft and £800 remaining because it contains no selections yet.

Budget means available funds before committing this preparation plan. Draft total includes all charges that will be committed at Start festival, not just services; expose a breakdown for services, bookings, staff, equipment and stock as applicable. Remaining = budget minus full draft total, using actual existing accounting so already-settled costs cannot be charged twice. Retry accounting must follow existing retry/reset rules, not charge last run's purchases again implicitly.

Choosing + Place creates an uncommitted ghost. Successful placement adds the service's cost to the draft; Cancel adds nothing. A second tap/toilet requires another explicit + Place (proposed initial interaction). Move retains the original item and cost, and Cancel restores its original position. Remove from draft subtracts the uncharged fee; it is not a cash refund. Charge once only on confirmed Start festival, using the existing start confirmation flow.

## Placement

Show the footprint, facing/entrance direction and access clearance, with green + tick for valid and red hatching + cross + a concrete reason for invalid. Do not rely on colour alone. The doorway block in the board is an example of a failure reason, not a new collision-rule specification. Runtime validity must agree with existing placement, boundary and access checks.

Provide visible Rotate 90°, Place and Cancel controls as well as R / Esc shortcuts. Camera controls must remain usable without accidentally placing. Choosing Place is disabled for an invalid ghost. The budget/readiness footer stays visible even when the drawer collapses. An active placement blocks Start with the explanation 'Finish or cancel placement'; never silently commit a ghost. The detail panel omits the repeated full footer for space; it is not a proposal to hide it in gameplay.

## Readiness and starting

Readiness summarises water, toilet, existing safety coverage, programme and sound rules. Water/toilet existence alone must not bypass their actual validation or existing coverage rules. Food and bar are optional; do not add new required services. Show expandable specific reasons and actions for unresolved requirements. Preserve any other existing start prerequisites rather than replacing them with these four shorthand rows.

Start festival remains visible in a persistent footer across preparation destinations. When unavailable, explain why and expose the reason to keyboard/screen-reader users; do not rely on a non-focusable disabled control as the only explanation. Insufficient remaining budget is a blocker. When ready, show an enabled action that leads to the existing confirmation with the complete charge summary.

## Retry and defaults

After failure, reopen the previous layout, bookings, staff and other choices as an editable draft. Retain perks under existing rules. Reset temporary service contents, occupancy and queues; do not restore a frozen running simulation. Banner offers Continue editing or Use defaults…; it is not an extra mandatory wizard step.

Use defaults restores the current standard service layout at normal cost, not a free setup. Preserve bookings/staff/other choices. If replacing a player layout, show confirmation with the old/new service counts and draft total/cost difference before enabling Replace layout. The board's wording 'Review the new draft total' represents that required summary, not permission to omit the figures. Keep my layout is the safe cancel action. Recompute readiness and budget after applying; defaults do not automatically start the festival or bypass insufficient funds. Use existing standard coordinates, not a newly invented default arrangement.

## Review boundary

Approve the UI direction through the coordinator before implementation. No additional user confirmation is needed when the coordinator approves. Builder should then verify smallest-resolution readability, focus order, mouse/keyboard placement, cancel/move accounting, multiple taps/toilets, singleton limits, readiness, defaults replacement and failure-retry restoration. No such runtime tests have been performed for this concept-only package.
