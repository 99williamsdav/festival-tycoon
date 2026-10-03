# Mosaic perk suite v1 — eight-card review

Historical eight-card review record. On 3 October 2026, six additional existing-catalogue artworks were integrated under user request and producer authorization. The active manifest now indexes fourteen cards; [batch 2 source/reviews](../mosaic-suite-batch2/) and [integration result](../../../../briefs/results/perk-mosaic-batch2.md) record that addition without a separate user aesthetic-approval claim. The original eight entries and runtime files remain unchanged.

The user and coordinator approved the original Something in the Water mosaic direction, then commissioned and approved these eight existing perks on 27 September 2026. See APPROVAL.md. Builder integration of the standalone artwork is authorized with native text and current layouts unchanged. Designer has preserved placeholder SVGs and the original water concept and made no game changes. No ninth perk, mechanics change or commit.

## Deliverables

- artwork/<perk-id>.png: standalone text-free mosaic panels, opaque landscape PNGs. These are the prospective runtime assets, not the card screenshots.
- cards/<perk-id>-744x1074.png: complete high-resolution review cards, with deterministic separately typeset catalogue text.
- small/<perk-id>-248x358.png: actual draft-size review cards.
- small/<perk-id>-250x142.png: actual shallow owned-card review, using 46 × 46 contain-fit artwork.
- contact-sheet.png: labelled full suite, including shallow-card comparison.
- owned-size-sheet.png: all eight shallow cards at actual pixel size.
- review.html and build-review.cjs: reproducible review layout and captures; not replacement production UI.
- manifest.json: stable perk IDs, exact title/effect, asset paths, original pixel dimensions and SHA-256 hashes.
- PROMPTS.md: complete shared prompt and per-asset subject text; built-in image generation, no API/CLI fallback.
- checks.json: catalogue-match and draft-text-fit checks.

## Composition / provenance

The approved card and logo-v2 were inspected and provided as local references. All eight raster artwork panels were produced with the built-in image-generation tool, one call per panel. The water panel is a landscape adaptation of the approved tap-and-cup composition, not a byte-identical crop; its original approved portrait remains untouched at ../perk-mosaic-concept-v1/something-in-the-water-mosaic-v1.png. Text was intentionally excluded from generated assets, then placed as ordinary native-style typography in the review renderer. No external art, fonts, brands, red-cross emblem or emergency-service insignia were requested.

Shared treatment: chunky ceramic tiles with warm cream grout, deep teal/turquoise, golden tile trim/sun accents, sage foliage; clear silhouettes distinguish capacity (paired vests/bags and addition symbol) from training (single vest/rosette, bag/lightning). Two standpipes indicate extra water access, tower indicates flow, tap/cup indicates water satisfaction, crowd/drop indicates faster thirst. Gold addition symbols are capacity cues, not medical emblems. No combo hints or new numerical tuning.

## Existing UI fit — do not redesign

Read-only source: C:/Projects/festival-tycoon/game/Main.Perks.cs. Draft card minimum 248 × 358, padding14; art panel216 × 145 with10 inner padding and KeepAspectCentered. Owned card250 × 142, padding9; art46 × 46 KeepAspectCentered next to19px title; effect14px below (232 × 66). The HTML is a size/legibility approximation of this existing contract, not a screenshot of a running Godot build. It deliberately uses the current smaller landscape art window rather than expanding into the much taller concept-card art area. No builder-owned layout change is proposed.

Use PNG assets with aspect-preserving contain fit and no stretching/cropping. Keep title/effect as the existing native labels, sourced from PerkCatalogue. Preserve the artwork at master resolution; apply normal linear filtering when downsizing. The 46px slot is supplemental recognition beside a readable name: ceramic grout and small details inevitably disappear at this scale. Do not turn the full mosaic into generic icons to compensate. No separate simplified thumbnail is included unless actual-size review demonstrates it is needed.

Exact copy is validated against C:/Projects/festival-tycoon/src/Festival.Simulation/Perks.cs before capture. £30 text is the existing separate staff-hiring cost, not a new card price or rarity treatment. No mechanics may be inferred from visual embellishments.
