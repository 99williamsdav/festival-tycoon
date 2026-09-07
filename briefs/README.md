# Foundation implementation briefs

Bounded assignments implement M0 sequentially. These are implementation instructions, not completed work. Begin with M0.01 and do not proceed until its implementation and evidence are accepted.

## How to use

Give a coding agent access to the whole repository, then assign the selected brief. A brief without its referenced specifications is not complete context. Each agent must inspect prior implementation and evidence before extending it.

Accept each result using its checks and short demonstration before assigning the next task. If a dependency is incomplete, report the specific gap; fix a small directly blocking defect with a regression check, or record a bounded prerequisite repair if it is substantial. Never mark an unmet dependency complete merely to continue.

| Order | Brief | Reviewable result |
|---|---|---|
| 1 | [M0.01 — Toolchain](M0.01-toolchain.md) | Buildable projects and launchable Windows shell |

## Shared execution rules

1. The full product rules live in `GAME_DESIGN_SPEC.md`; architecture in `TECHNICAL_SPEC.md`; numeric defaults in `CONTENT_AND_BALANCE.md`. A brief narrows current implementation scope; it does not change future product requirements. Historical discovery notes are not a competing task list.
2. Preserve user changes and inspect existing code before adding new abstractions. Build on previous briefs' actual interfaces. The suggested repository layout is guidance; do not move working code solely for cosmetic conformity.
3. Hold the key requirements: one simulated person per ticket/wallet, pure simulation independent of Godot presentation, reproducible same-build behaviour, no duplicate money/stock/reservations, saved authoritative state, low-poly farm direction, fixed camera rotations and persistent overlays.
4. Engine/C# choice is the current implementation baseline. Exact tick rate, navigation strategy and tuning values are engineering proposals to validate. Start with specified defaults; record measured reasons for changes and update relevant contracts/tests. Do not casually substitute an engine, population multiplier or core product behaviour.
5. Implement only the state and features this brief exercises. Do not scaffold the entire future game or duplicate simulation logic inside the graphical application. Fixture commands and scripted intentions must be clearly identified as development fixtures.
6. Every new authoritative field must enter state hashing and save capture/restore once persistence exists. Keep timestamps, screenshots, camera and other cosmetic state out of gameplay hashes. Explain any excluded state.
7. Add targeted tests for acceptance behaviour and regressions, including failure paths. Record exact commands and results. Exporting is not proof of launch; a screenshot is not proof of queue/financial correctness; a deterministic empty state is not proof of simulated behaviour.
8. Follow the execution environment's permission rules for tool installation. Keep setup reproducible, pin versions and report unavailable tooling accurately. If a required visual/hardware check cannot run, complete other authorized work and mark that check Unverified.
9. Each task ends with a demonstration route, changed-file summary, evidence and limitations. Update `PROGRESS.md`. Use Not started, In progress, Implemented—verification pending, Accepted, or Blocked; Accepted requires evidence for every required check. Note whether manual QA was performed by the agent or user.
10. Stop at the assigned boundary. Do not start the next brief, publish, merge or make external project changes unless separately requested. Ordinary local implementation and verification are part of an assigned brief.

## Evidence format

Keep detailed task evidence in `briefs/results/M0.xx.md` when implementation runs. Include build/content identity, modified areas, command exit/results, tests and failures, a demonstration, and remaining limitations. Link screenshots or benchmark artifacts when relevant. Do not create empty evidence reports in advance or imply tests have already run.

The progress table is a navigation aid, not proof by itself. A future agent must inspect implementation and linked results before relying on an Accepted row.
