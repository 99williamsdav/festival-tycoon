# Food van — no overhead awning assembly

26 September 2026: user explicitly requested removal of the food-van awning because it hides customers from camera angles. Coordinator authorized this exact minimal correction. Drinks-stand canopy is unchanged. No new model or broader redesign.

## Exact minimal runtime instruction

In `game/Main.ImmersionAssets.cs`, `InstantiateImmersionVendor(true)`, omit instantiation of `lwf_food_van_serving_flap_v1.glb` and its position/rotation/AddChild. The raised flap at Godot (2.15,2.25,1.17), rotation X −105°, is the projecting overhead awning. Keep the chassis, fascia, vendor root and assembly offset unchanged. The chassis already contains the counter and dark serving aperture; fascia retains the header silhouette. No flap-file deletion or GLB/source modification is necessary.

The omitted flap also supports a future closed hatch, but no closed-hatch state is used by the current always-open operating vendor. Omission is limited to that operating assembly; historical source and reusable module remain available.

Retained exact assets:

- `assets/runtime/environment/lwf_food_van_chassis_v1.glb` SHA-256 `40f67435b506b0e127588e518a53d04c69de224f09599354674cbfa95ddf36bb`.
- `assets/runtime/environment/lwf_food_van_fascia_v1.glb` SHA-256 `92f906654d0f4bd7b8b13b718889f6e70d31b3b5fe9f9307d7db522e31911f63`.
- Historical omitted flap `assets/runtime/environment/lwf_food_van_serving_flap_v1.glb` SHA-256 `5c9b9ec08c26b9215ac61dee047c085abfb8582b517c15cf671e6cf6c105f133`.

Keep `ApprovedFoodVanAssembly` Godot offset (−2.15,0,0), original chassis origin and fascia mount (2.15,2.49,1.18). No footprint/service/queue/rotation/economy changes are decided by this art correction. Builder owns the separately requested site relocation and authoritative anchors/save behaviour.

## Review and visibility limits

Four local Blender reviews use unchanged adult customers, current game camera yaw 45/135/225/315° and approximately 38.9° elevation. `01-view-45.png` is the default front-facing view; `04-view-315.png` is the alternate front-facing rotation. The overhead obstruction is absent. Rear-facing views 135/225° still have natural chassis/body occlusion: removing an awning does not make an opaque van transparent. These are local assembly previews, not final-site in-game evidence. Builder must verify default/rotated views at the new bottom-right dirt-track site.

Original approved geometry/materials/provenance remain in `art/reviews/M0.06/food-van-v1/`. `build_review.py` imports exact existing modules without writing/re-exporting them. No new GLB, gameplay edits, drinks-canopy changes or commit by Designer.
