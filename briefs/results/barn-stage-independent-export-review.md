# Barn Stage independent exported-build addendum

**PASS — actual copied Windows build accepted for the approved Barn scope.** Independent GPT-6 Astra MEDIUM review, 13 October 2026. No new blocking finding. This bounded addendum follows `barn-stage-independent-review.md`; it accepts the actual EXE/data delivery and its native evidence, without authorizing normal campaign Barn programming or adding a manual-playthrough gate. No implementation or delivery files were changed by the reviewer.

## Exact delivery and source

- Source commit: `99d2c8a9d9e0902051136f6dec463a990d73b146`.
- Accepted source checkpoint SHA-256: `CA87AACB0DD43E84C1F38A5165267E058FAA8E6CE4CE27B8984F0C0DA7B809E1`. Independently rechecked all **80 source entries**, with no mismatch; scoped source remains at the accepted version.
- Actual delivery: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-barn-stage-2026-10-13/FestivalTycoon.exe`.
- EXE size: **198,669,016 bytes**; SHA-256 `114E90F636029055D613018B306D3A318D34496057D74C2643EE7F3C2AA220D2`.
- Independently checked all **194 payload files** (EXE plus **193 adjacent data files**) against `reports/evidence/barn-stage/exported-build-manifest.json` and `artifacts/windows-barn-v1`: **zero missing files, size discrepancies or hash mismatches**. The delivery contains exactly 194 files and **no ZIP**.
- Build-manifest SHA-256: `11D07A275F1A05D2695223471BFF9C50BC113E486731D6187FD56DDD571EF88F`.

The recorded running process was PID 32656 at the actual delivery EXE path, with `--fixed-fps 30 --quit-after 1400` and the explicit `--capture-barn .../artifacts/barn-exported --capture-size 1280x720` arguments. `barn-export-process.json` hash is `C73249F14A25CB4A5F2CDB06FBD459C5961DD3BD20251E965EC56BFCE93D9634`. This ties the retained evidence to the copied EXE rather than a source-only/editor run.

## Native evidence checked

Evidence is retained in `reports/evidence/barn-stage/exported`. Independently verified every one of the **37 file entries** in its manifest, including byte sizes and SHA-256 hashes. Evidence-manifest hash: `0A5800245EA5C32408CBD673F0017CF6E0A8E7C2799EE474ECE27F998934D3DE`.

All **54/54 native checks pass**. The 450-observation trace spans tick 8002 through 9200; the actual first set begins at **tick 9000, frame 374**. Independently decoded/probed the MP4: **450 frames, 1280×720, 30 fps, exactly 15 seconds**. Movie hash: `D6B3A0109490AE1A1FF774C241FDF5D9E297133110AAF702820A4B7148048289`. Reviewed sampled decoded movie frames, the entire 24-view camera contact sheet, and the occupancy/re-entry/empty images against the accepted source evidence.

All four camera azimuths at sizes 18/62 are represented in day, late light and overridden rain. All **24 camera/picking checks** pass, each recording 15 exact actor ray hits through the shared picker. Nine explicit fade surfaces retain backface culling and full shadow-only twins; opaque frame/floor/signs and per-instance isolation pass. Band west-facing/deck height, listener arrival, distinct stage/generator ownership, exact pause and menu-over-paused-host pose checks pass.

The empty and deselected traces first rise from alpha .30 after **1045 ms** and **1042 ms**, respectively, and restore alpha 1. Re-entry occurs at frame 1194 while recovery alpha is .3466667, then returns to .30. Sole guest, performer and staff occupancy checks pass. The orange shape retained on the deck is the independently instantiated electronic drum hardware established in the source review, not an extra actor. Occupancy images explicitly reposition actors with authority held; their unchanged programme card is not used as movement evidence. Diagnostic save isolation and interior-versus-apron coverage checks pass.

The visual result is consistent with source acceptance: layered slate/stone translucency remains apparent; close zoom crops roof edges; people are small at default zoom; opaque frames and neighbouring buildings can occlude individuals. No new missing-texture, gross lighting or Barn presentation regression was visible in the reviewed evidence. This is the specified finite camera/weather/fixture coverage, not an exhaustive crowd or natural-rain-balance claim.

## Exporter and runtime records

The exporter is recorded as exiting 0 but emitted **two** `ERROR: Condition "f.is_null()" is true. Continuing.` messages from `get_multiple_md5` during existing texture imports. They are retained in `verification/barn-exporter-log.txt`, SHA-256 `6757C8D42D4CF808EB4CC0A8FF70EA617C6B875C36418A00EB9CD1E5ADCBD976`. This was not a warning-free export. The completed payload, matching copied hashes, actual clean runtime and visual checks provide the bounded acceptance evidence despite those continuing exporter messages.

The actual application log ends with `BARN_EVIDENCE_COMPLETE` and contains **zero ERROR/WARNING messages**. Runtime numeric exit status was **not measured**: `barn-export-exit.json` records `observedExitCode: null` for PID 32656 (hash `F68C5D70B9D1118643B29D56DF307F56DBC0DCC89D3BA7A20C0E8060DE5802F4`). The GUI launcher's reported 0 must not be presented as the application's observed exit code. Acceptance rests on the identified running EXE, completed capture marker, passing assertions, complete valid evidence and matching payload.

Current unrelated texture imports, package lock, lead-outfit source and seven UID files remain outside the Barn scope; review made no changes to them. Source acceptance and this exported-build addendum together satisfy the commissioned delivery gate. Normal play retains Trailer alone or Trailer/Pond; Barn programming remains confined to the labelled controlled fixture, which cannot save as a campaign.
