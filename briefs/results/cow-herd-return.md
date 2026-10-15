# Cow herd return — hired steward candidate, 15 October 2026

During an active assigned HERD RETURN, the hired steward now follows a goal about two metres ahead along the cow's existing route. Movement away from the cow is capped at its unchanged9mm/tick once established; excessive lead waits. Fetch, joining from behind, reassignment and released staff retain ordinary movement. The goal refreshes on40tick boundaries, with bounded160tick avoidance retries. Near the original gate, the steward stands aside so the final cow cell stays clear. No cow speed, completion, ownership/deadline/needs/priority/economy or save schema change.

The fix is restricted to hired assigned stewards with cow.drive intent and the matching Driving cow. Existing manual and automatic player movement is unchanged. Active hired returns wait/replan rather than using the ordinary300/600mm lateral separation displacement; all other navigation retains it. Legal pathfinding, terrain sweeps, occupancy and cow avoidance remain in force. All pacing is derived from existing state; no saved speed overrides or timers.

Evidence: reports/evidence/cow-herd-return/herd-return.mp4 is a21second20fps native replay (420actual rendered frames) with one-time labelled starting positions, normal paid preparation and HerdCow command. No per-frame pose injection, root pull or faster cow. positions.json/CSV pair with the previous rendered state. Establishing the lead, completion and return-to-post are present; established driving gap1.373–1.933m, average1.599m. Process exit0 and empty stderr. Rawframes remain ignored under artifacts/cow-herd-native. This scripted fixture does not claim manual balance validation.

Five new tests cover actual gap/step sizes/completion, ordinary fetch/join, curved legal route, pause/load/determinism, assignment loss/no lingering pace, and waiting/resume with another cow. Existing two-cow convergence, deadlines, cow spacing and obstacle avoidance tests are retained. Source/native independent review remains required; final regression results follow.

## Investigation history and bounded scope

The initial interpretation extended lead pacing to the automatic player. A full-day regression then earned81Headliner against its unchanged <Headliner assertion. Unchanged baseline and hired-only isolation both pass that exact assertion. The extension was withdrawn; original StewardAutopilot.cs and StewardShift.cs are byte-equivalent to HEAD. No score/AI handicap or existing assertion was changed.

The discarded automatic-player fixture exposed a separate preexisting save-validation defect: an owned cow checks its owner against hired Disorder.Stewards even when the owner is the distinct hands-on player. A valid player-owned herd can therefore be rejected. Independent reviewer recommended recording it separately: neither that validator repair nor the auto-only test is included here. Hired active-return save roundtrip and determinism are covered by the retained test. No new task or authorization is implied.

One initial temporary-file save failure disappeared with the same native temporary-save access as the passing baseline. Retained investigation logs are not presented as passing candidate validation. Original Survival references are never re-recorded.

## Final source validation

Builder55/55 combined tests pass (4m14s), including unchanged immutable Survival state/hash/event parity and the original <Headliner grade assertion. Fresh independent Astra Medium review PASS:16/16 focused tests, clean game Rebuild with zero warnings/errors, all33 source/protected/evidence/immutable pins match. Native clip and six paired original captures inspected. Report: briefs/results/cow-herd-return-independent-review.md. Copied Windows export is a separate subsequent gate.

