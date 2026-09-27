# Guest colour variation — designer-approved concept

27 September 2026. Scope authorized by user through coordinator. Standing rule: all in-game graphics have a design-led concept before Blender/in-game building. `attendee-colour-concept.png` was generated and visually reviewed FIRST; `palette-contract.json` was then authored. The subsequent Blender run only read existing sources to audit UV semantics; it did not recolour, save or build assets. Original source palettes, GLBs and Blender files are preserved. Runtime colour implementation belongs to Builder after this handoff. No additional user visual approval gate; review in game.

## Art direction and approval

Designer approves **four coordinated clothing tuples** (field teal, clay rust, meadow sage, dusk blue) and **four natural hair colours** (dark brown, soft black, dark blonde, auburn). All sixteen tuple/hair combinations are approved for both existing body variants. Same available palette for both sexes, independent of gameplay traits. No neon, pure-white garments, arbitrary generated RGB or random mixing of tee/patch/trouser choices. Teal/dark-brown remains the unchanged original combination.

The built-in image-generation skill produced the reference-based colour study; exact prompt and input paths are in `PROMPT.md`. This is AI-assisted design concept art, not an actual Blender recolour or runtime screenshot. Generated details vary slightly (especially hair outline, faces and proportions); NONE of those shape differences are approved. Preserve the actual male-v5/female-v3 mesh geometry, hairstyles, faces, body/outfit proportions and pose set exactly. Only colour direction is approved. Image lighting/swatch pixels are not authoritative colour values: use the sRGB hex contract.

## Exact material / palette mapping

`palette-contract.json` is machine-readable and authoritative for every hex value. Existing body material has an embedded **96×8** palette: twelve horizontal swatches, each 8×8. Slot n spans x=8n through 8n+7 inclusive, all eight rows. UV centres `(8n+4)/96, 0.5`. Nearest filtering, clamp, opaque; retain existing roughness/no extra glow or metallic finish. Hex represents sRGB bytes; do not double-apply gamma conversion.

| Slots | Region | Policy |
| --- | --- | --- |
| 0, 1, 2 | Skin, arms/hands, face; female mouth uses 1 | Protected byte-for-byte |
| 3, 4 | Tee base/light reserve | Approved clothing tuple |
| 5, 6 | Existing flush rectangular patch base/light reserve | Same clothing tuple; no new graphic |
| 7, 8 | Trousers and hip base/light reserve | Same clothing tuple |
| 9 | Eyes AND shoes | Protected byte-for-byte; not hair |
| 10, 11 | Hair base/light reserve | Approved hair pair only |

All twelve source pose files pass a named-region UV audit (`slot-audit.json`, `audit_slots.py`). Currently male geometry uses 0,3,5,7,9,10; female additionally uses 1 for mouth. Light partner slots are reserved/unreferenced by current models but defined coherently for the palette format. Do not add highlights, alter UVs or geometry to use unused slots. Skin shades stay untouched, including the female mouth. Hair slot10 is separate from shoe/eye slot9; no mesh split is needed.

## Builder implementation contract

- Use body-only texture/material variants. Start with a copy of the original texture and replace only the allowed slots. Do not tint whole body/material/root; that would recolour skin and eyes. Props keep their own original materials; staff and performer kits are excluded.
- Preserve original imported resources. Cache at most sixteen colour combinations per compatible base material identity, shared across guests and all pose resources; avoid mutating a shared original and avoid per-frame/per-guest texture allocation. Preserve material flags from source rather than rebuilding arbitrary defaults.
- Derive two independent cosmetic indices from existing stable guest identity/campaign seed with domain-separated deterministic hashing; do not consume authoritative RNG or use process-randomized string hash. Keep assignment fixed across all poses, save/load, retry and visual reconstruction under the existing identity semantics. No new saved appearance fields or gameplay state. Use a second hash/domain for hair rather than `guestId % 4` reused for both, which would permanently pair clothing and hair.
- Both characters may use every palette; no sex-dependent colour pool. No skin-tone variation in this slice, customization interface, new patterns/outfits/hairstyles, role cue changes or prop recolouring.
- Keep contract IDs/order versioned; changing order later changes deterministic appearance and needs deliberate handling.

## Bounded checks before delivery

Verify all sixteen generated palettes preserve protected slots0/1/2/9 byte-for-byte, alter only allowed bytes and retain dimensions/alpha. Check no original resource hash changes. Render both bodies across four clothing/four hair options (a bounded mixed grid plus close-ups of soft-black and dark-blonde hair suffices for appearance), and one guest switching all poses to prove palette continuity. Check skin/eyes/mouth/shoes/cup/tray unchanged, save/load/retry stable, staff/performers unchanged. Do not infer gameplay/FPS correctness from this concept or read-only UV audit.

No runtime palettes, GLBs, mesh edits or game code were produced by the designer in this concept task. Existing renderer lighting will differ from concept; exact contract colours and unchanged shapes take precedence.
