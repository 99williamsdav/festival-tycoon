# Mosaic perk suite — batch 2

Six original PNG artwork masters produced with the built-in image_gen tool on 3 October 2026, one separate image-generation call per card. Each call used the same water-card anchor and logo reference as suite v1. The exact prompt set is in PROMPTS.md. No API/CLI generation fallback was used.

The artwork masters are opaque, text-free landscape 1536 × 1024 PNGs. Native card names and effects remain separate from the artwork. manifest.json records current exact catalogue text, subjects, dimensions, SHA-256 hashes and the three requested review sizes. Effect text was checked against current src/Festival.Simulation/Perks.cs.

Run build-review.cjs using the bundled Node runtime to regenerate review.html, six 744 × 1074 review cards, six 248 × 358 draft previews, six 250 × 142 owned previews, contact-sheet.png, owned-size-sheet.png and checks.json. The review layout follows the historical suite-v1 pipeline and is not a screenshot or redesign of the current game UI.

placeholders contains simple matching SVG fallbacks. The masters, not the full review cards, are intended for game/assets/ui/perks/<id>.png. Integration does not change perk effects, balance, draw logic or card layout.
