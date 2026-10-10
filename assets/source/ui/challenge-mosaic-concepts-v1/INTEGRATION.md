# Fixed festival challenge reveal — 10 October 2026

Established coordinator relayed explicit user authorization to integrate Heatwave B at Tier 1 and Heavy Rain A at Tier 2, then explicit instruction to resume the reveal after the separate Windows ZIP export. The user authorized the screen design directly; no new art generation or aesthetic gate was needed. This integration record supersedes the handoff's earlier selection-only scope. Original files, prompts, selection and provenance are unchanged.

Runtime selected masters are exact copies under `game/assets/ui/challenges/`. Both preserve 1536×1024 pixels, no titles baked into the masters and full 3:2 display. All other masters/comparison boards/references remain here as source. Heatwave A/C are possible future pleasant-weather art only, not Heatwave replacements or permission to implement pleasant weather.

`FestivalChallenges.ForTier` is fixed: Tier 1 Heatwave B, Tier 2 Heavy Rain A. No challenge draw/RNG mutation. Random challenge selection may be considered later; not implemented.

The initial audit found no rain mechanics. During this work, independent rain commits f750705 / 14e0076 / 9e8f38f were merged into the shared repository by 4053b6f. The final reveal therefore describes actual current rain effects (wet guests, mud, ponchos/shelter) rather than retaining the obsolete limitation caption. This commission does not add or modify rain simulation or tier progression.

Acknowledgement is one scalar on the existing preparation state, reset on the existing retry path and fresh festival creation. Its command updates core state directly without replacing projected people. It uses SessionHost's ordinary save cadence; normal manual/start/milestone saves also preserve it. Resume from an acknowledged durable save skips the reveal; restarting before the acknowledgement has reached a durable save can show it again. Save namespace/compatibility advances to r0-build-v59; old files remain intact, no migration.

[Integration, tests and native exported evidence](../../../../briefs/results/challenge-reveal.md).
