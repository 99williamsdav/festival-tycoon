# Selected cow v3 asset — 7 October 2026

The user requested "Commit the asset to git", selecting reviewed cow v3. The producer relayed that authorization on 6 October and resumed the interrupted commit on 7 October. The approved concept and original pre-approval handoff are preserved in the sibling cow-asset-concepts-v1 package. V3 is selected; v1/v2 originals stay in the delivery workspace and are not runtime candidates.

Reusable runtime asset: game/assets/characters/animals/lwf_cow_v3.glb, its native .import and the extracted embedded palette PNG/.import. No gameplay scene references, spawning, AI or Council mechanics are added. No EXE is needed for this asset-only commit.

The editable .blend, construction script, verification script, palette, selected GLB, supplied build/verification reports and sixteen Godot review PNGs are retained. manifest.json records selected source/runtime/concept SHA-256 values. Source and runtime GLBs match exactly; the extracted runtime palette matches the selected source palette.

Idle, WalkPreview and Alert are exported PREVIEW actions, not finished locomotion. The in-place walk still lacks final body weight transfer, speed matching, turns/terrain and gameplay foot locking. Existing directly posed leg hierarchy and overlapping head/limb attachments remain. No exhaustive self-intersection or controller-clearance check is claimed.

## Reproduction and evidence

Run Blender 4.1.1 with --background --python build.py from this package to rebuild. The concept sibling is mirrored at the expected path. Rebuilding may produce new .blend/GLB file bytes; the committed manifest identifies the selected delivery, not a bit-for-bit reproducibility promise.

verify.py retains the supplied validation and adds configurable paths: COW_HISTORY_ROOT must point to the original art workspace containing cow-asset-v1 and cow-asset-v2; COW_VERIFY_OUTPUT can point to a fresh evidence JSON. Those older packages are dependencies only for historical comparison, not for construction. This commit's Blender check passed 111 source animation samples, normalized weights, finite vertices, hoof contact targets within 2 mm, GLB reimport, unchanged prior package hashes and v3 ear/leg geometry checks.

The original godot-check/proof.gd and review/comparison.html are supplied provenance and still reference the original delivery workspace/prior v2 review; adjust paths before rerunning or viewing comparisons from a relocated repository. The sixteen selected review PNGs remain directly usable. The new repo-native verify-import.gd under reports/evidence/cow-v3-integration loads the actual reusable runtime asset: one mesh/surface/material, skin, 19 bones, palette and all three finite midpoint preview poses passed in Godot 4.7.2. This is asset validation, not gameplay acceptance. Godot logged an environmental certificate-store warning while the check completed successfully.
