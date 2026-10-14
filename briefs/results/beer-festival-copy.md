# Beer Festival display copy

14 October 2026. User-authorized wording-only change relayed by the established coordinator.

The display/effect copy is exactly **Charge 50% more for beer and everyone seems happy about it**, without added punctuation. `PerkCatalogue` supplies the draft, owned-card and gain/lose descriptions in `Hud/PerkPanel.cs`, so all current native descriptions receive the same string.

Updated the active fourteen-card artwork manifest, the Beer Festival entry in the batch-2 manifest and its two HTML descriptions. Regenerated only Beer Festival's full/draft/owned PNG reviews plus the two composite sheets. The Designer workspace batch-2 manifest/HTML and those five derived images mirror the update. Its original eight-card suite has no Beer Festival entry and remains historical.

Stable id `beer-festival`, name, rarity, weight, standalone runtime/source artwork and artwork hash remain unchanged. All mechanical source is unchanged: existing beer price multiplier, no-teetotallers and increased-drinking behavior remain; no satisfaction mechanic was added. Original prompts, historical decision/provenance records, the existing all-card renderer/checks and other individual card previews remain untouched. The bounded renderer in `reports/evidence/beer-festival-copy/render-review.cjs` typesets the new copy in the existing HTML layout; it does not regenerate artwork or claim a native game screenshot.

Verification: 3/3 `BeerFestivalTests` and `PerkArtworkTests` passed. These exercise pricing/abstainers, increased sales and save restoration, and all fourteen runtime artwork hashes/catalogue descriptions. Existing tests required no edits. Debug game build passed with zero warnings/errors. Exact DOM copy, text fit at 248x358 and 250x142, and all three PNG sizes (including 744x1074) passed. All 24 protected-file hashes matched; only Beer Festival's effect field changed in each manifest. Retained logs, preview checks and scoped hashes are under `reports/evidence/beer-festival-copy`.

Fresh independent GPT-6 Astra MEDIUM review **PASS**, recorded in `beer-festival-copy-independent-review.md`: exact copy, preserved mechanics/artwork, all 44 checkpoint hashes and five preview images verified. No Windows export was made for this request; existing EXEs do not include this new wording. Barn programming/allocation and unrelated dirty work are outside scope and unchanged.
