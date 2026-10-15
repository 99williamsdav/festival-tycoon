# Classic weather core — independent review

15 October 2026. Reviewer: fresh GPT-6 Astra context, medium reasoning. Scope is exactly the 15 files pinned by reports/evidence/classic-weather/core/review-checkpoint.json, base 55cc443db621fb591003b1b5f7c317898d6aa040. All 15 SHA-256 pins matched at static-review entry. Game/UI work is excluded.

## Current result

PASS for the pinned source-core scope: no actionable findings. All 15 source hashes also matched after independent execution. This is source-core approval only; it does not approve the game/UI or a playtest handoff.

Reviewed the separate keyed weather draw, ordinal selection, five authoritative Celsius samples, integer interpolation, half-open saved rain intervals, immutable records and cloned arrays on capture/restore, exact regeneration validation, and the medical/stock/shelter/ground-cover save validators. Existing Heavy/Downpour enum values and parameters remain intact; appended Showers values match the approved plan. Non-hot Classic chooser growth now agrees with categorical physiology. Rainy Classic Tier 1 ground-cover exceptions are explicitly Classic-and-rain guarded, preserving Survival gating. Temperature does not introduce a new physiological factor.

Restore validation checks campaign ordinal before seeded-dependent checks; strict regenerated samples reject missing, malformed, reordered, duplicated or changed data. No read operation uses a session gameplay RNG. Shared timestamp mapping retains 80 ticks per second, and duration formatting remains independent of the mutable presentation mapping.

Independent execution after exclusive-lane clearance:

- Existing compiled ClassicWeatherTests: 11/11 passed, zero skipped, 8.497 seconds. This includes actual rainy Tier 1 matting/stock saves and live rain/wetness/heat behavior. Evidence: reports/evidence/classic-weather/core/reviewer-tests.txt.
- Isolated compiled probe referencing the existing Simulation.dll: 144/144 checks passed across all six conditions. Explicit forecast-validator rejection was checked for missing forecasts, null/empty/null-element/extra/reversed samples, wrong version and invalid condition. Capture and restore array isolation, interpolation and lower/upper clamping were exercised. Evidence and source: reports/evidence/classic-weather/core/reviewer-probe.txt and reviewer-probe/.
- Final hash verification: reports/evidence/classic-weather/core/reviewer-hashes.json.

The builder separately reports 41 earlier passing cases and a final 13/13 run (11 Classic weather cases plus two complete immutable Survival parity fixtures covering 63 boundaries) after the ground-cover capability change. These are builder evidence, not reviewer execution. The final filter did not discover a separate GroundCoverTests class; no such class coverage is claimed. The independent reviewer did not regenerate any baseline or modify product/tests source.

Compatibility boundary: old fixed-mild Classic saves lack the mandatory new forecast and will be rejected. The builder confirmed the separate upcoming UI scope uses r0-classic-v100 while preserving old r0-classic-v99 files; all Survival namespaces remain v99. This report does not verify that UI change. Forecast UI, shared timestamp consumers, visual evidence and save-slot routing require their separate review before a playtest handoff.
