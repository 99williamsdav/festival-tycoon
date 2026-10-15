# Accepted combined playtest — 15 October 2026

The reviewed Windows build includes the approved Classic booking/ticket layout, all six approved forecast mosaics, the lone-bar display name Bar, and hired stewards maintaining a short lead during assigned HERD RETURN. Fetch, reassignment and released movement retain ordinary pace. Cow speed and completion, player controls/autopilot, grading, weather rules and save schemas remain unchanged.

Launch C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-herding-forecast-2026-10-15/FestivalTycoon.exe with its adjacent data_Festival.Game_windows_x86_64 folder. No ZIP. Source code commits: herding 531f91fe, forecast art 0d14dd94, bar 5d686e26; prior accepted Classic tickets remain included.

Validation:

- Builder 55/55 combined tests, including unchanged immutable Survival state/hash/event parity and original autopilot grade assertion.
- Independent herd source review:16/16 focused tests, clean rebuild with zero warnings/errors,33 checkpoint pins matched.
- Forecast source/native: builder262/262 at720p and independent262/262 at1080p; all six exact master hashes and fit verified.
- Actual copied EXE herd replay:420 rendered frames,21seconds, completion, exit0 and empty stderr. Independent comparison confirms all 420 authoritative position records exactly match the reviewed source fixture.
- Independent actual copied EXE:558/558 checks (tickets100, Classic109, weather262, Survival63, save warning24), five final runs exit0 with empty stderr;55 captures generated; key captures visually inspected. All 2,480 pins match:194 new payload,2,052 source inputs,194 prior r2 payload,4 older EXEs,26 preserved inputs and10 immutable references.

Payload:194 files,301,806,639 bytes excluding README. EXE SHA256:926C641FF9290797D5469B5973C62636BB295DB1297BD73D2C759A2DB4824329. This is a working-tree export with preserved unrelated input edits. The export log retains two warnings about missing Main.LavHoseEvidence.cs.uid and Main.StrawMarker.cs.uid being recreated from cache; no export ERROR was found. Final runtime logs are clean. Initial restricted reviewer launches had Godot log/cache write errors and one interrupted weather run; those logs are preserved separately and excluded from final clean results.

Reports: briefs/results/cow-herd-return-independent-review.md, forecast-mosaic-suite-independent-review.md and herding-forecast-export-independent-review.md. Export checkpoint and final clean evidence: reports/evidence/herding-forecast-export/. Copied-EXE gameplay clip: reports/evidence/herding-forecast-export/builder-exe-herd/herd-return.mp4, with paired position records and keyframes. Only fixture starting positions are staged; subsequent movement is simulated with existing rigs. No manual full-day balance, accessibility or extended performance claim.

All earlier accepted folders and original Survival references are preserved. The existing Survival rain-HUD overflow remains outside this bounded change. A separate preexisting player-owned-cow save validation issue is documented in the herd source brief; it is not part of this hired-steward change. No new feature, automation, task or other-chat message was created.
