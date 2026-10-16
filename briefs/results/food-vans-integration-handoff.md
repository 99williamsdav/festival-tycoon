# Approved food vans — integration handoff

16 October 2026. COMPLETE: independent source/native and actual copied-EXE reviews passed.

## Change

The approved rounded cream/blue chips trailer, timber/terracotta pizza wagon and ribbed plum/mustard curry cab van replace their corresponding existing traders' complete body assemblies. Exact production GLBs are used; no art redesign. Editable source, artwork, reproducible scripts, production review and evidence are archived under `assets/source/environment/food-van-production-2026-10-16/`. Original package documents remain unchanged; added INTEGRATION.md records the game boundary.

The curated models include fascia, names, roof icons and kitchens, so old assembled decorations are omitted. Legacy palette substitution is removed. Existing separate curry chalk menu, server rigs and food props remain separate; chips/pizza had no separate chalk-menu asset previously. Curry steam attaches to the two delivered named vent markers and retains existing simulation-driven busy/pause behavior.

Assembly offset (−2.15,0,0), server (0,.47,.88)/yaw PI/scale one, service point (0,0,2), queue logic and original 13×7-cell reservation remain unchanged. Curry extends .21 m beyond the old physical hitch but .13 m inside its reservation; rear steps extend .01 m beyond the old physical rear with .30 m reservation margin. Only the ray-picking target expands to 6.5×4×3 m inside the original X/Z reservation, covering cab and roof. No authoritative navigation, economy, pricing or simulation source changed.

## Verification

- Complete 73-file original package is byte-identical in the archive; frozen production manifest: all 67 listed entries verified; all three imported GLBs retain the approved hashes, one mesh/four surfaces and expected triangle counts (2,370 / 2,066 / 5,244).
- Existing server/trader/immersion regressions: 41/41 passed. No new simulation tests or baseline repins.
- Final native run: 155 food/server checks plus 39 preparation/file-roundtrip checks = 194/194, exit zero, empty stderr. All three normal simulation sales completed with arrived queue-head owner at the actual service centre, matching customer-held receipt/visible food, empty server retract, no duplicate transaction and exact file roundtrip.
- Six van/sex combinations exercise 2,178 imported runtime pose samples for feet/held-prop clearance/tilt and 1,116 additional full-clip/crossfade foot samples. These are native pose/property checks, not a runtime whole-body triangle-intersection proof. Production's separate Blender BVH evidence remains archival asset evidence.
- All four actual placement quarter-turns, placement ghost, shared click resolver, physical roof/cab ray targets, trader rebuild/pick-registry cleanup, curated material preservation, separate menu and curry markers pass.
- All 24 sixth-run production images and six final shadow comparison images independently inspected. Inspection6 is deliberately enlarged; minimum18/default62 use normal camera sizes. Real sale images face the actual serving side. Controlled comparisons identify directional-shadow bands at some angles, including the pizza roof at nearest normal zoom. Production lighting is unchanged; shadows-off frames are diagnostic only. No claim of pristine shading.

`--capture-food-vans` selects the architecture evidence save root during command-line configuration, before the first SessionHost. All ordinary/development profiles were pinned before any diagnostic: 233 files and 52 directories. Exact content/file-set/directory-set comparisons after each checked run remain unchanged. No user save was deleted, restored or overwritten. Existing standalone `--capture-servers` was not used because it does not isolate its save directory.

Early failed attempts are retained: wrong diagnostic navigation API, assumed chalk menus on all variants, Survival duplicate-trader rule, native particle type-name casing, mandatory perk-before-trader setup. These were harness repairs; no production/economy workaround. Fifth run passed but captured live scenes from the back; sixth camera correction affects diagnostics only. The optional ArchitectureReady callback occurs after ordinary perk/layout steps and leaves existing callers unchanged.

## Review and delivery

Source review: `briefs/results/food-vans-integration-independent-review.md`. Actual copied Windows EXE review PASS: `briefs/results/food-vans-exe-independent-review.md`. Observed 321/321 checks (food 194, Survival 66, Classic stress 61), three exits zero and empty stderr. All 30 food, eight Survival and 19 named stress images plus four sampled clip frames inspected; other clip frames were not individually reviewed. Final guards pass all payload/source/preservation pins and 233 player files/52 directories. Asset commit `6d361ea6`; game commit `7b467c6d`. Evidence is under `reports/evidence/food-vans/`; test/build/native/export logs are retained in artifacts and mirrored into evidence.

No coffee, tractor, crowd-position, gameplay, performance or unrelated feature work is included. Staff doors are static visual geometry. Roof/fascia naturally obscure server faces at some views; default zoom makes fine details tiny. Diagnostics use real game handlers and normal simulation with explicit stepping, not a manual playthrough. Existing frame-rate limitations remain; no performance claim. No ZIP or other-chat message.


## Export checkpoint

Completed export and byte-checked copy: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-food-vans-2026-10-16/FestivalTycoon.exe`, with adjacent `data_Festival.Game_windows_x86_64`. Payload: 194 files / 305,056,555 bytes (README excluded). EXE SHA256 `E36EEB8F410F09102CA4849F42938FCD1016BFA28FA92426494D8AE93038D8AF`. Checkpoint records 2,123 source pins and 1,300 preservation pins.

Seven immutable-reference/test pins in the older tractor checkpoint were superseded by intervening committed crowd-standing/test work before food integration began. `inherited-reference-changes.json` records their original hashes, inherited hashes and commits. Original tractor checkpoint is retained unchanged. No reference was restored or re-recorded in this food task; current inherited versions are separately pinned. This is a working-tree export including those earlier committed changes and 95 preserved unrelated inputs, not a clean standalone food-only branch.

After export completed at 22:16:07, commit `e1ebc93d`/merge `c62f8dc1` changed eight shared source files. Initial independent preflight correctly stopped before launch on those mismatches. Original checkpoint/payload hashes remain unchanged. Exact exported bytes of all eight files were reconstructed from `7b467c6d`, matched against their original checkpoint hashes, and archived under `reports/evidence/food-vans/exported-source/`; the override manifest and guard explicitly distinguish them from later live files. This playtest does not include that later merge. The guard still rejects any unexpected live-source differences. No live source was restored or overwritten.

