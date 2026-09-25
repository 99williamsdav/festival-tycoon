# R0.04 layout gate — 25 September 2026

Status: **Stopped before implementation/commit.** The user requested moving the large barn to the rear field, the approved security post near the entrance, and the first-aid tent farther back, with Jordan and Riley physically based at their facilities and returning after responses. The trial below was fully reversed under the explicit stop instruction; current code remains at `23d4b9a`, and the restored test project builds with zero warnings/errors. The unrelated first-aid v1 files were untouched.

## Tested layout candidate

| Item | Proposed centre/duty position | Result |
|---|---|
| Large barn | Visual centre `(0, -23)` m; blocked navigation rectangle `x=-11..11`, `z=-30..-16` m | This would clear the active front field, but changes the authoritative scenario content hash. |
| Approved security post | Grid `(114,178)`, world `(-6.75,25.25)` m; Jordan's walkable public-front duty cell `(114,182)`, `(-6.75,27.25)` m | Proposed left of the gate road; not rendered or accepted because the coupled trial stopped. |
| First-aid tent | Grid `(116,108)`, world `(-5.75,-9.75)` m; Riley's front entrance `(116,116)`, `(-5.75,-5.75)` m; rest point `(120,116)`, `(-3.75,-5.75)` m | Physical medic travel became too long for an existing treatment route. |

The trial also added physical return-to-base intents after completed medic/security responses. No new graphics, steward yoke/cue, or hearing mechanics were introduced. None of these trial edits remain in the working tree.

## Exact blockers

- `dotnet build tests/Festival.Tests/Festival.Tests.csproj --no-restore -warnaserror` passed with zero warnings/errors on the trial. The focused `DisorderIncidentTests|MedicalIncidentTests|FarmSceneTests` run failed `DistressedPerformerCanReceivePhysicalMedicTreatment`: expected `MedicalStage.Treated`, actual `Critical`. This is an unsafe medical-timing regression for a real physical response, not merely a screenshot difference. Two disorder fixtures also diverged under changed travel/geometry (`SeparatedConfrontationCannotInjureARemoteOpponent` and `FailedCalmingThenGuestInjuryCannotRestartStaleSecurityFight`); the run was stopped rather than spending an unbounded loop retuning fixtures.
- The current R0.04 save slots have content hash `0b7dfbade3fd86317ca3b85ae162e4cf126cf16ec23c1dd211d6a435b672ccd7`, ruleset `r0-disorder-v10`. The save loader rejects a changed content hash unless an explicit migration is implemented. Moving the authoritative barn and navigation obstacle changes that hash. The inspected current R0.04 slots were pre-start, but a safe migration must also define what happens to any active older route crossing the new rear-barn footprint; no silent hash reuse, teleport, or overwrite is acceptable.

## Decision needed

Choose an exact farther-back first-aid area that still passes the existing physical-treatment deadline, **or** authorize a specific medical-response timing change and its safety tests. Separately, approve an explicit old-R0.04-layout save migration policy for preparing and active saves, including a no-teleport refusal if a saved actor occupies a newly blocked cell. After those choices, retest the barn/post/tent placements and all gate, stage, water, medical and security paths together; capture beginning/active default-angle views before committing. No R0.05 work should begin from this gate.
