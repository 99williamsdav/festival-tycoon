# Festival Tycoon

> **SUPERSEDED / PAUSED FOR REFERENCE (23 September 2026):** The annual prestige-tycoon handoff below is not the active product plan. Use [ROGUELIKE_DESIGN.md](ROGUELIKE_DESIGN.md) and [R0 briefs](briefs/R0/README.md). Preserve this text as design history; `TECHNICAL_SPEC.md` still applies where the pivot does not conflict.

A British-inspired festival-management simulation: inherit a farm, take a loan, stage a tiny event, then build a legendary festival through music, money, mud and individual-driven chaos.

## Developer-ready design handoff

Historical handoff follows. Its statement that no game code exists is no longer current repository status. Read it only as the archived alternative:

1. [Game design specification](GAME_DESIGN_SPEC.md) — complete player experience, annual loop and interconnected game rules.
2. [Technical specification](TECHNICAL_SPEC.md) — engine decision, architecture, data contracts, deterministic simulation, navigation and saves.
3. [Content and balance catalogue](CONTENT_AND_BALANCE.md) — initial values, worked economy, facilities, unlocks, objectives, incidents and writing examples.
4. [Implementation and acceptance plan](IMPLEMENTATION_PLAN.md) — sequenced milestones, scope boundaries, verification and developer starting brief.

[Discovery notes](DESIGN_NOTES.md) preserve the discussion history. Where they differ, the consolidated specifications above take precedence. Remaining numerical assumptions are labelled for playtesting; the core product and system decisions are resolved.

Visual direction: low-poly 3D, lush British countryside, four camera orientations; tactile festival paperwork and persistent management overlays. Target: Windows desktop, Godot .NET with an independent C# simulation. Browser support is a future port rather than a guaranteed export.

The [original visual study](STYLE_STUDY.png) is included for reference; use the left panel.

## Start implementing

Use the [foundation implementation briefs](briefs/README.md), beginning with [M0.01 — Reproducible project and Windows build](briefs/M0.01-toolchain.md). Each brief has targeted reading, dependencies, scope, exclusions, acceptance checks and a ready-to-paste assignment. Work through them sequentially and record actual results in [foundation progress](briefs/PROGRESS.md).
