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

Fresh GPT-6 Astra MEDIUM independent source review and actual copied Windows EXE addendum both passed with no actionable findings. The reviewer independently reran the six new tests (6/6 PASS), verified all 194 delivered/export payload hashes and all 13 accepted source hashes, and ran the exact delivered EXE: 37/37 native checks, eight screenshots and measured exit code 0. Reports: `development-tier-starts-independent-review.md` and `development-tier-starts-exported-exe-review.md`.

## Windows export

Source commit: `e4f4b69f81b1c85ce8dff0682df4997f02608ebc`. Copied delivery: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-development-tiers-2026-10-14/FestivalTycoon.exe` plus 193 adjacent data files. All 194 payload hashes match the export, and no ZIP was created. EXE size 198,669,424 bytes; SHA256 `440EFA0E1703B8CE5A9BB7AD8991E907F2880D534121CFE3864C6D7DA43B5740`.

Exporter exit 0. Four `f.is_null()` / `get_multiple_md5` “Continuing” messages occurred while importing existing character textures; the complete log is retained. The actual copied EXE passed 37/37 native checks, generated eight screenshots, emitted `DEVELOPMENT_TIER_EVIDENCE_COMPLETE`, and exited with measured process code 0. Its runtime log is clean. Process executable/arguments were observed and retained in `exported/process-proof.json`. All 13 accepted source hashes (12 reviewed files plus the generated diagnostic script UID) remain unchanged after export. These are bounded scripted UI/save checks, not a manual whole-festival playthrough or performance claim.

This EXE also includes the separately committed Beer Festival wording change (`d7d9351`), which did not have its own export, and the accepted prior Barn/fisticuffs work. Their gameplay scope has not been expanded.

The reviewer's own sandboxed EXE run recorded one blocked `user://` shader-cache creation error; rendering, all captures/checks and exit code 0 still completed. The Builder run with approved native access had a clean runtime log. Both logs are retained, and the review treats this as a sandbox cache-access limitation.
