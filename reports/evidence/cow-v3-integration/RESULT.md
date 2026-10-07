# Cow v3 asset validation

Completed Blender check before the interrupted turn, preserved and reused: passed 111 source animation samples; normalized weights; finite vertices; hoof targets within 2 mm; Blender GLB reimport; prior v1/v2 hashes unchanged; v3 ear shape/leg geometry checks.

Fresh Godot 4.7.2 repo-native import check: COW_V3_REPO_IMPORT_OK. One skinned mesh/surface, 19 bones, loaded palette and Idle/WalkPreview/Alert animations with finite midpoint bone positions. The only engine error was inaccessible root certificate store; validation exited 0.

Selected source .blend, GLB, palette and concept hashes match their supplied reports. Runtime GLB and extracted palette match selected source bytes. Source/runtime/concept hashes are recorded in assets/source/characters/cow-asset-v3/manifest.json.

Scope: reusable asset only. Preview actions are not finished locomotion; no gameplay spawning/AI/Council changes or EXE. Existing unrelated dirty files are excluded.

Direct GLB structure check passed: GLB v2 header/length, 2,546 indexed triangles, one mesh/material/skin, 19 joints and exactly Alert/Idle/WalkPreview.
