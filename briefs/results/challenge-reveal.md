# Fixed challenge reveal — 10 October 2026

The approved Heatwave B mosaic now introduces Tier 1; Heavy Rain A introduces Tier 2. A full-screen dark teal reveal shows the complete 3:2 artwork, separate concise title/effect text, focused **Prepare festival** action and **Return to menu**. It precedes the existing perk draft and preparation UI. No random challenge selection or new progression is added.

User authorization was relayed by the established coordinator, including the instruction to resume after the separate Windows ZIP delivery. Selected masters are byte-identical to [SELECTION.json](../../assets/source/ui/challenge-mosaic-concepts-v1/SELECTION.json); [source, variants, prompts and integration provenance](../../assets/source/ui/challenge-mosaic-concepts-v1/INTEGRATION.md) are preserved. Heatwave A/C remain future pleasant-weather art candidates, not active Heatwave art or an implemented pleasant-weather challenge.

## Lifecycle and state

- Initial menu entry, fresh campaign/Pond Stage trial and existing Next festival route show the fixed tier challenge before perks or preparation. Same-tier retries reset acknowledgement and show that tier's same artwork again.
- Continuing acknowledges the attempt through SessionHost and then exposes the existing required perk draft. It neither chooses/skips/rerolls a perk nor starts a festival. Cash, roster, RNG streams and ticks remain unchanged by acknowledgement.
- Acknowledgement is one scalar on the preparation core; it does not replace the projected roster. It is saved with normal cadence/manual/start/milestone state, without a new per-click synchronous save. Ordinary panels/menu resume and acknowledged saved-state restore do not repeat the reveal. Closing before acknowledgement is durably saved may show it again after restarting from the prior save.
- Underlying CanvasLayers are hidden while the reveal is open, world input is blocked, and the main frame returns before simulation/world presentation advances. The dismissal event is consumed to prevent input reaching the field. Normal keyboard focus is given to Continue; existing Godot UI actions provide keyboard/controller navigation.
- Save compatibility and namespace advance from the current rain build's v58 to **r0-build-v59**. Older files remain intact; no migration.

Tier 2 and its next-festival lifecycle were already implemented. During this task, independent rain commits f750705/14e0076/9e8f38f merged at 4053b6f. The final Heavy rain copy therefore describes real current behavior: showers wet guests and the ground, mud develops, ponchos and shelter help. This reveal commit does not implement or change those mechanics. The earlier audit's “rain unavailable” observation is superseded by that merge.

## Verification

- Game and test builds: zero warnings/errors. Windows release export completed.
- **22 targeted tests passed**: four challenge mapping/state/RNG/restore/retry tests, perk and draft-save regression tests, Tier 1 carry/next-tier restrictions and the actual Tier 2 fatal-hearing/Favour retry case. No failed/skipped cases in the final selected runs.
- Actual exported EXE fixtures passed for both tiers at **1280×720 and 1920×1080**. Each confirms reveal before the pending draft, keyboard focus, hidden underlying HUD, no simulation/camera change under modal input, complete 3:2 artwork and non-overlapping controls, continuation to the unchanged perk hand, ordinary menu/resume, saved-state acknowledgement and fresh-festival reset.
- Native screenshots were inspected at both sizes, including the perk draft after Continue. No manual festival playthrough or physical controller hardware test is claimed.
- A broader initial run exposed the corrected roster bug and an obsolete hot-weather assertion, then timed out on two full rainy-day endurance cases. Final focused runs verify this bounded change; they do not claim completion of those unrelated endurance cases.
- All **194** delivered EXE/data files match the tested export by SHA-256.

[Exported screenshots, JSON checks, logs and hashes](../../reports/evidence/challenge-reveal/). Developer route: `--capture-challenge <output> --start-tier <1|2> --capture-size 1280x720`. Other existing art/evidence fixtures retain their direct preparation entry; ordinary play and tier/trial entry use the reveal.

Windows playtest: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-challenge-reveal-2026-10-10/FestivalTycoon.exe` with adjacent data directory. The previous ZIP checkpoint is unchanged and does not contain this feature. No new ZIP was requested for this delivery. Export includes the current merged workspace; this commit is limited to reveal UI/state, selected art/source and bounded evidence.
