# Development tier starts

The main-menu Pond Stage trial link is replaced by a native development tier picker, using the existing Steward Only picker style. Tier 1 starts a genuinely fresh management campaign. Tier 2 opens Heavy Rain, then ordinary preparation on the Trailer and Pond stages, after the canonical transition from an actual completed Tier 1 reference. The box-office ticket stub now displays the active tier.

The recorded reference was approved by both the coordinator and the user: seed 20260922, two stars, 33.6172% satisfaction, completed departure without death or Council ending. It is eligible under the existing survival gate. The reference factory restores the complete hash-validated state and calls `CreateNextFestival`; no simulation runs on menu clicks and no money, standing or relationship grants are supplied separately.

Cash: £353.35 actually closed Tier 1, with £200 principal still owed. Tier 2 adds only £750 of its own ticket receipts, opening at £1,103.35. Tier 1 net operating/cash result was £153.35; its £158.15 headline profit excludes £4.80 of unsold stock write-off. The original £450 starting funds are not added twice. Reputation 14, scene credibility, three learned acts and actual relationships (DJ +10, Harmonium +1, Daisy −1), their gig history and previous layout carry through. No kit, water-share reward or popularity shift was earned. Perks, placed layout, bookings, staff and stock begin fresh. See `src/Festival.Simulation/Development/README.md` for full provenance and reconciliation.

Normal progression, Council retry, save compatibility and mode-specific save directories remain in use. The development starts use the management host/save namespace, as the replaced trial did; saving a development start replaces that management campaign. Tier 2 deliberately reuses the recorded campaign identity/seed. The legacy approximate `CreateDevelopmentFestival` factory remains unchanged for CLI and Steward Only fixtures. No Tier 3 or Barn performance allocation is added.

## Validation

- Six new simulation tests passed: normal eligibility/transition equivalence, accounts and ledger reconciliation, learned state and reset behavior, Tier 2 unlocks and affordable two-stage preparation, actual Council failure/retry, and ordinary-command reproduction.
- Eighteen existing next-festival/challenge regressions passed after resolving sandbox access for two temporary save-file replacement checks. The first bounded run had 22 passes and those two access failures; their approved rerun passed 2/2. This is 24 distinct passing cases, not 26.
- Offline recorder reproduced the entire embedded snapshot byte for byte, SHA256 `1802C92ADA0692E67553B0EC15E7374F9B1513952F895ACC8D31A07DE7D5567D`.
- Clean native game build, zero warnings/errors. Actual Godot source run passed 37/37 native button, focus, reveal, preparation, save-file, resume, reset and parked-host checks, with eight screenshots.
- Four bounded existing save/reset and Steward Only/All-Rounder regressions passed. Total distinct simulation cases: 28.

Evidence: `reports/evidence/development-tier-starts/`; exact owned source files pinned in `checkpoint.json`. The diagnostic route writes only under its requested evidence directory. Screenshots and checks are scripted native execution, not a manual playthrough. The Council test deliberately injects a casualty to exercise the real hearing/Favour/retry flow; the reference recording contains no injected state.

Fresh GPT-6 Astra MEDIUM independent source review passed with no actionable findings; the reviewer checked all 12 owned source hashes, eight native screenshots and independently reran the six new tests (6/6 PASS). The actual copied Windows EXE addendum is pending, so no accepted executable handoff is claimed yet.
