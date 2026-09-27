# Attendee poses v2 — integration handoff

27 September 2026. User authorized pose production and subsequent guest integration without another visual approval gate; review will be in-game. Sources: approved male neutral v5 and latest female neutral v3. Earlier assets remain unchanged. Hair, face, body/outfit and palette are preserved exactly; only arm/hand geometry changes in active poses. Relaxed is the original neutral stance, not a new body redesign.

## Runtime contract

Agreed with Builder task 01a07b97-26db-7571-a2fe-48db1ea69494 before final export. `manifest.json` is authoritative: integrate ONLY the twelve `lwf_attendee_*_v1.glb` files it lists. Shorter GLB filenames are build intermediates, not another asset set.

Five presentation states per sex: relaxed, drink_hold, food_hold, drinking, eating. Drinking has beer and soft product-specific meshes (six files per sex). `variants.male` and `variants.female` contain entries with state, optional product, filename, source Blender filename, SHA256 hashes, embedded-palette hash, triangle/vertex budget and attachment dictionaries.

All bodies: metres; ground-centred origin; Godot -Z forward; identity body placement; one mesh, one surface, one embedded-palette material; no rig/animation. The optional Attachment_Cup/Food empties are metadata, not props. Apply manifest Godot-root-local position, XYZ rotation in degrees and identity scale ONCE to the separate existing prop below the stable attendee root. Do not compose the socket and manifest offsets twice. Positive X rotation is +55 degrees for beer consumption; all other attachments have identity rotation. No baked cup/tray geometry exists in the final body exports.

Drink hold supports beer and soft with the same hand fit. Food hold/eating support the unchanged chips tray under its centre; eating raises the other empty pinching hand to the mouth. No new bite prop/inventory is introduced. Beer consumption uses an open-rim contact; soft consumption keeps the lid/straw upright. Attachment height differs by body to preserve existing faces. Prop files and hashes are recorded in manifest.props; they are not rescaled or redesigned.

Builder owns stable-root visual swaps, deterministic guest appearance, actual consumption eligibility/cadence, picking, interruption/suspension/load handling and Godot integration tests. Preserve legacy staff/performers. No walking/collapse/medic animations, customization or bar integration are included here.

## Budgets (body only)

| Pose | Male triangles | Female triangles |
| --- | ---: | ---: |
| relaxed | 1206 | 1842 |
| drink_hold | 1410 | 2046 |
| food_hold | 1254 | 1890 |
| drinking beer/soft | 1410 each | 2046 each |
| eating | 1282 | 1918 |

Female variants exceed the earlier 1800 neutral planning range; that is disclosed, not treated as a hard runtime budget pass. No FPS claim.

## Validation and visual evidence

`verification.json`: twelve Blender GLB reimports pass; world vertex sets, counts, embedded palettes, UV row and attachment translation/rotation checks pass. Every untouched named region is exactly preserved from its base. Cup-hand sample checks (vertices, triangle midpoints and centroids against nominal tapered polygon body) find zero sampled penetration for both products. Tray sample checks find zero intrusion; nominal underside support gap is 0.3 mm. These are bounded geometry checks, not exhaustive collision certification. Beer closest rim vertex to mouth target is about 0.6 mm; soft straw surface vertices are 3 mm from its centreline mouth target. Female mouth target uses existing mesh mouth; male uses an explicit lower-face target because its mesh has no named mouth feature.

Actual Blender renders: `review-male.png`, `review-female.png`, `contact-male.png`, `contact-female.png`, individual front/side/beauty/contact PNGs, and `male-eating-mouth.png` / `female-eating-mouth.png`. `gameplay-scale.png` uses native 1920x1080 render pixels at 32 m orthographic span and 38.9-degree elevation. It is a staged Blender scale check, not Godot gameplay. Props and fine finger detail remain small, and hair can occlude female side-view consumption; no hair edit was made.

Editable `male-*.blend` and `female-*.blend` include separately named REVIEW_PROP objects for visual context plus review lighting/ground. Re-export only the body and optional socket, as `verify_export.py` does. The palette remains packed in the body source/export. Build with Blender 4.1.1: `build_poses.py`, then `verify_export.py`; assemble sheets using `assemble_review.cjs` (Node/Sharp), optional mouth closeups via `render_mouth.py`.

Development checks caught an overly broad hand-group selection and a validator syntax typo; both were corrected before the successful full export/validation run. Mouth contact was refined after the first rendered pass. No production game files were changed by the designer. Integration/import/gameplay validation remains the Builder's next step.
