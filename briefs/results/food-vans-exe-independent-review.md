# Independent copied-EXE food-van review

Status: PASS — independent actual copied Windows EXE review complete, 16 October 2026. No unresolved material finding within the approved model-integration scope. Existing directional-shadow banding remains an explicit accepted limitation.

## Executable and provenance

Executed `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-food-vans-2026-10-16/FestivalTycoon.exe` with adjacent `data_Festival.Game_windows_x86_64`. SHA256: `E36EEB8F410F09102CA4849F42938FCD1016BFA28FA92426494D8AE93038D8AF`. Independently counted 194 runtime files / 305,056,555 bytes, excluding README.txt. Every payload hash matches the original export checkpoint.

Three sequential actual EXE processes used Start-Process -WindowStyle Hidden -Wait -PassThru with captured stdout, stderr and exit files. Application arguments followed `--`: `--capture-food-vans`, `--capture-survival-architecture`, then `--capture-classic-stress`, each with its absolute evidence destination and `--capture-size 1280x720`. No `--path game`, fixed FPS, rebuild, export or product edit was used. Each process exited zero with empty stderr. Runtime reports identify the copied executable path; stdout identifies Godot 4.7.2 Compatibility on the NVIDIA RTX 3050 Ti.

The initial preflight stopped before execution because eight live source files had changed after export. Original failure evidence remains in `independent-exe/pins-before.json`. The bounded resolution preserved the original checkpoint hashes and archived exact exported bytes for those eight files under `exported-source/`; no live source was reverted. Independently verified all eight archived byte hashes against the ORIGINAL checkpoint and normalized content against git `7b467c6d`. The diff from that commit to later merge `c62f8dc1` names exactly those eight game/simulation files. Commit metadata places `e1ebc93d` at 22:16:46 and `c62f8dc1` at 22:17:03, after the recorded completed export. This accepts the frozen export, not equivalence to the later live source.

Audited the updated guard: it validates original hashes for eight archived inputs and the remaining 2,115 live source files, explicitly lists the eight live differences and rejects unexpected differences. Before execution and after all runs it passes 194 payload, 2,123 source and 1,300 preservation pins. Independently verified the seven inherited tractor reference/test changes against `6d361ea6^` and their declared inherited hashes; none was restored or re-recorded. Provenance details are in `independent-exe/provenance.json`. The export checkpoint SHA256 at final review is `C5A608AA14B23C9CE21C94F8EA90C3E03A7DE9A1DBEFA8F078F2929493EB46F0`.

## Runtime and visual coverage

- Food: 155 food/server plus 39 architecture checks = 194/194 passed. Approved import resources have four surfaces and 2,370 chips / 2,066 pizza / 5,244 curry triangles, assembly offset (-2.15,0,0), curated materials and only the existing curry chalk menu as separate assembly child. Checks cover rotations, placement, physics roof/cab picking, rebuild cleanup, both server rigs, animation/prop constraints and pause/steam behavior.
- Three normal simulation offers and sales passed. Queue owner is queue head, navigation action Arrived and actual position within 10 mm of the service centre. Recorded owners are chips34, pizza34 and curry36, each at (4250,17250) mm. Each sale reaches empty-server retract, matching customer-held receipt and visible food, no duplicate transaction, and an exact file roundtrip. Reviewed the harness assertions as well as outputs.
- Survival: 66/66 passed (actual count, superseding the estimated63). Management, all-rounder manual/autopilot switching, steward tiers, movement and file roundtrips pass, including resumed management view cues.
- Classic stress: 61/61 passed. Paid full roster, all three live stages, stage-specific generator isolation, trader/build UI, extra-post cleanup, full festival completion, nine set results, save roundtrips and next-festival cash carry without a new grant pass. 420 clip frames were generated; recorded frames0–419 run from tick11004 to12680 with100 attendees/133 people. This is the explicitly labelled existing development preset.

Total observed: **321/321 checks**, rather than the estimated318.

Independently viewed all30 food PNGs: twelve placed quarter-turn inspection views, six normal minimum18/default62 views, six real offer/sale images and six shadow-on/off comparison images. Reviewed labelled contact sheets and additional full-resolution sale/offer views. Approved silhouettes, fascia/name/roof artwork, body proportions and serving relationships are present. All8 Survival PNGs and all19 named stress PNGs were inspected, plus live clip frames0000,0140,0280 and0419. Contact sheets are retained in `independent-exe/`; originals remain in the mode directories. Full clip motion and every one of420 frames were not reviewed.

The broad-panel/roof bands are visible under normal shadows, including pizza at nearest normal zoom, and disappear in the controlled shadows-off comparison. The harness switches only directional shadow enablement and restores it with the same model instance and authoritative state; shadows-off images are diagnostic only. No pristine shading claim. Roof/fascia can obscure server faces; fine detail is tiny at default zoom; staff doors remain static. Existing performance limitations remain, with no performance claim. Runtime diagnostic stepping is not a manual playthrough or a whole-body triangle-intersection proof.

## Preservation and verdict

Player-save guards before execution, after Survival and after all runs pass:233 files and52 directories, no content/file-set/directory-set differences. Final `pins-after.json` and `saves-after.json` retain the results. The actual copied executable and adjacent payload remain unchanged; source acceptance uses the explicit eight archived exported inputs rather than silently accepting later live edits.

PASS for the approved chips/pizza/curry production-model integration and copied-build handoff, within the stated scope and limitations. Reviewer changed evidence/report files only. Exclusive execution lane released; no process remains running from this review.
