# Foundation implementation briefs

Ten bounded assignments that together implement M0 from [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md). These are implementation instructions, not completed work. No coding agent has been dispatched and no game code has been written by creating these briefs.

## How to use

Give a coding agent access to the whole repository, then paste the assignment at the end of the selected brief. A single copied brief without the referenced specifications is not the complete context. Start with M0.01 and proceed in order. Different agents can take successive tasks; each must inspect the prior implementation and evidence before extending it.

Accept each result using its checks and short demonstration before assigning the next task. If a dependency is incomplete, report the specific gap; fix a small directly blocking defect with a regression check, or record a bounded prerequisite repair if it is substantial. Never mark an unmet dependency complete merely to continue.

These tasks are deliberately sequential because they share contracts, save state and project structure. Do not send all ten simultaneously to independent agents or ask one to build the entire game. Subsequent milestones should receive new briefs informed by the actual M0 implementation.

| Order | Brief | Reviewable result |
|---|---|---|
| 1 | [M0.01 — Toolchain](M0.01-toolchain.md) | Buildable projects and launchable Windows shell |
| 2 | [M0.02 — Deterministic session](M0.02-deterministic-session.md) | Repeatable clock, IDs and commands |
| 3 | [M0.03 — Content catalogue](M0.03-content-catalogue.md) | Validated authored scenario data |
| 4 | [M0.04 — Atomic purchases](M0.04-atomic-purchases.md) | Reconciled wallets, stock and transactions |
| 5 | [M0.05 — Save round-trip](M0.05-save-roundtrip.md) | Resumable state and safe file recovery |
| 6 | [M0.06 — Farm and camera](M0.06-farm-camera.md) | Four-view farm scene with persistent HUD |
| 7 | [M0.07 — Navigation](M0.07-single-agent-navigation.md) | One agent traverses the actual scene |
| 8 | [M0.08 — Service queue](M0.08-service-queue.md) | Five agents queue and pay once each |
| 9 | [M0.09 — Fifty-agent integration](M0.09-fifty-agent-integration.md) | Runnable crowd, time controls and live saves |
| 10 | [M0.10 — Benchmark and gate](M0.10-benchmark-and-gate.md) | Measured performance and M0 acceptance report |

## Shared execution rules

1. The full product rules live in [GAME_DESIGN_SPEC.md](../GAME_DESIGN_SPEC.md); architecture in [TECHNICAL_SPEC.md](../TECHNICAL_SPEC.md); numeric defaults in [CONTENT_AND_BALANCE.md](../CONTENT_AND_BALANCE.md). A brief narrows current implementation scope; it does not change future product requirements. Historical discovery notes are not a competing task list.
2. Preserve user changes and inspect existing code before adding new abstractions. Build on previous briefs' actual interfaces. The suggested repository layout is guidance; do not move working code solely for cosmetic conformity.
3. Hold the key requirements: one simulated person per ticket/wallet, pure simulation independent of Godot presentation, reproducible same-build behaviour, no duplicate money/stock/reservations, saved authoritative state, low-poly farm direction, fixed camera rotations and persistent overlays.
4. Engine/C# choice is the current implementation baseline. Exact tick rate, navigation strategy and tuning values are engineering proposals to validate. Start with specified defaults; record measured reasons for changes and update relevant contracts/tests. Do not casually substitute an engine, population multiplier or core product behaviour.
5. Implement only the state and features this brief exercises. Do not scaffold the entire future game or duplicate simulation logic inside the graphical application. Fixture commands and scripted intentions must be clearly identified as development fixtures.
6. Every new authoritative field must enter state hashing and save capture/restore once persistence exists. Keep timestamps, screenshots, camera and other cosmetic state out of gameplay hashes. Explain any excluded state.
7. Add targeted tests for acceptance behaviour and regressions, including failure paths. Record exact commands and results. Exporting is not proof of launch; a screenshot is not proof of queue/financial correctness; a deterministic empty state is not proof of simulated behaviour.
8. Follow the execution environment's permission rules for tool installation. Keep setup reproducible, pin versions and report unavailable tooling accurately. If a required visual/hardware check cannot run, complete other authorized work and mark that check Unverified.
9. Each task ends with a demonstration route, changed-file summary, evidence and limitations. Update [PROGRESS.md](PROGRESS.md). Use Not started, In progress, Implemented—verification pending, Accepted, or Blocked; Accepted requires evidence for every required check. Note whether manual QA was performed by the agent or user.
10. Stop at the assigned boundary. Do not start the next brief, publish, merge or make external project changes unless separately requested. Ordinary local implementation and verification are part of an assigned brief.

## Evidence format

Keep detailed task evidence in `briefs/results/M0.xx.md` when implementation runs. Include build/content identity, modified areas, command exit/results, tests and failures, a demonstration, and remaining limitations. Link screenshots or benchmark artifacts when relevant. Do not create empty evidence reports in advance or imply tests have already run.

The progress table is a navigation aid, not proof by itself. A future agent must inspect implementation and linked results before relying on an Accepted row.
