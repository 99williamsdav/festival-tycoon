# Final held trio — exact dual approval

On 26 September 2026 the user explicitly approved the final trio ("approved") and the coordinator separately approved the same exact versions. This record supersedes historical pending status in the preserved preview README/technical manifests. Exact source/runtime/provenance handoff is authorized; no redesign or commit.

| Approved runtime | SHA-256 | Full W × D × H | Triangles |
| --- | --- | --- | --- |
| `assets/runtime/props/lwf_chips_tray_v1.glb` | `0bb9ebb82a2b3558a450beb84d9b8f89467c5b67e603a1c759c69cb0d95167a3` | 183 × 123 × 60.85 mm including chips | 212 |
| `assets/runtime/props/lwf_soft_drink_cup_v1.glb` | `b0570c93ea5cba3c7d297111e3c070d66ac4e0f6cd24edede37c4e3b77fe9416` | 94 × 94 × 217 mm including straw | 240 |
| `assets/runtime/props/lwf_beer_cup_v2.glb` | `e6fd2e115eef76797b97a76759b57687c8ed0a25baced7acd805df1dd25adc8c` | 92 × 92 × 152 mm | 252 |

Editable final source: `assets/source/props/lwf_held_props_trio_lager_v2.blend` (SHA-256 `ce343624a2a1d5fdb86eb3a56decf49e3d2b919571fcc013fb2ccfc061811de8`). Original source and deterministic build/revision/verification recipes live under `assets/source/props/held-props-trio/`, with v1 and lager-v2 sibling directories. The final source contains unchanged chips/soft drink and only golden lager beer. Old brown beer v1 is historical review only, NOT approved for integration and is not copied into runtime.

## Attachment contract and limits

Metres, glTF Y-up, one mesh node per prop, applied transforms, no rig/animation/arms/collision. Cup local origin is body mid-height; 150 mm bodies extend Godot Y −0.075 to +0.075 m. Soft drink straw reaches Y +0.142 m. Tray local origin is centre of the nominal 180 × 120 × 50 mm tray; underside Y −0.025 m. Grip-centre proposals do not define a verified adult hand socket or holding pose. Builder owns presentation attachment; simulation owns held-item identity, consumption and vendor rules.

Clear beer shell alpha 0.22 and rim 0.65, with opaque amber-yellow lager and pale foam. Isolated v1 Godot import passed; revised beer Blender reimport passed with unchanged geometry/bounds. In-game transparency sorting, lighting and attachment remain unverified. At 32 m camera framing the cups are only ~5–6 px wide and tray ~11 px; fine details are not reliable identity cues. Use UI text as needed, not silent physical rescaling.

Review/provenance: `art/reviews/R0.05e/held-props-trio/` preserves both original and lager revision checkpoints. Preview adults remain unchanged scale context, not a holding animation. Original project-authored mesh/materials, no external downloads/textures. Approval does not imply vendor gameplay/economy decisions.
