# Herding and forecast Windows export — independent review

**PASS — 15 October 2026.** No blocking findings in this bounded copied-EXE review. Reviewed the actual `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-herding-forecast-2026-10-15/FestivalTycoon.exe` and adjacent data against `reports/evidence/herding-forecast-export/export-checkpoint.json`, source commit `531f91fe96d9da77d470906b895638e5b8518ec7`. EXE SHA-256: `926C641FF9290797D5469B5973C62636BB295DB1297BD73D2C759A2DB4824329`.

The payload contains 194 files totalling 301,806,639 bytes, excluding README. This is the declared working-tree export with preserved unrelated import/lock inputs, not a clean-checkout claim. Existing source-herding and final-six forecast review reports remain intact and retain their original scope/history.

The reviewer independently launched this copied executable, without `--path game`, for five tracks in isolated evidence directories. Final clean runs all exited 0 with empty stderr:

| Track | Passed checks | Captures |
| --- | ---: | ---: |
| Tickets | 100 | 12 |
| Classic loop | 109 | 9 |
| Weather | 262 | 22 |
| Survival | 63 | 8 |
| Save warning | 24 | 4 |
| Total | 558 | 55 |

Final execution records, arguments, timestamps, checks and captures are under `reports/evidence/herding-forecast-export/independent-clean/independent-exe-*`; consolidated results are `independent-clean/summary.json`. Visual inspection covered all six forecast conditions, including approved Heatwave v2 and darker HeavyRain v3; booking stat bars, ticket results and accounts; Classic menu/live scene; original Survival rain challenge/live scene; and dismissible save failure followed by recovery. The six mosaics fit the existing forecast slot without crop, distortion or collision. The retained Survival art is distinct from the forecast replacements.

The builder separately ran the actual copied EXE's 420-frame hired-herd fixture, with exit 0, empty stderr and completion. The reviewer independently compared all 420 authoritative position records with the reviewed source fixture: they match exactly. Executable identity is recorded in the exported fixture data. Reviewed exported keyframes show the short lead and completion/return to ordinary movement. Evidence is `reports/evidence/herding-forecast-export/builder-exe-herd/`; no duplicate herd process was launched by the reviewer.

Hash verification before and after runtime review passed for all 194 new payload files, 2,052 product/source files (including generated `Main.CowHerdEvidence.cs.uid`), four older EXEs, 26 preserved dirty inputs, ten immutable Survival references and all 194 files of the previous accepted r2 payload. All **2,480 final pins match** in `independent-clean/pins-after.json`. The retained export log matches its checkpoint hash. No source, payload, reference or original review report was edited by the reviewer.

Initial restricted launches completed tickets and Classic but could not write Godot's user log/cache. The reviewer stopped only the identified owned weather fixture; that interrupted run is not counted. These initial logs remain outside `independent-clean` as environment diagnostics. Final runs used normal native filesystem access, new isolated output directories and clean stderr. Export-time diagnostic history remains in the original export log and is not presented as runtime output.

Approval is for this exact exported payload and bounded fixture evidence. It does not claim a manual balance, accessibility or extended performance playtest. Final README/current-task handoff metadata is a separate pending readback check.

## Final handoff metadata readback — PASS

15 October 2026: the final metadata gate is complete. All 15 entries in `handoff-metadata-pins.json` matched at readback: launch README, current-task prefix with prior history retained, final handoff, source/forecast/review reports and their workspace mirrors, and the 20-byte generated cow-evidence UID. Launch path, adjacent-data requirement, source commit identities, EXE hash, 194-file/301,806,639-byte payload, test/check totals and caveats agree with the reviewed evidence. Reverified all 2,480 product/preservation pins; no product or payload changed.

Two wording findings were repaired and rechecked: handoff/current-task now distinguish 55 captures generated from key captures visually inspected, and the handoff/export-log summary retain the two missing-UID cache warnings instead of claiming a warning-free export. The actual log has zero export ERROR lines; the five final runtime runs remain clean. Initial restricted/interrupted diagnostics remain retained and excluded from passing totals.

No new build or native run was needed. Readback records are `reports/evidence/herding-forecast-export/final-metadata-pins-verified.json` and `final-metadata-review.json`. This addendum intentionally changes this report's own hash after the readback; remirroring this exact final report and refreshing its metadata pin is the expected administrative follow-up, not a product change. No blocking findings remain.
