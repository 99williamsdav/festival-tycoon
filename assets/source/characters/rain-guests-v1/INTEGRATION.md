# Rain guests v1: integration

No code has been changed. File and class names below are the current ones (`CrowdBodies.cs`, `CrowdBodies.Walk.cs`).

## 1. The four rain clips

The clip library shares the guest rig's skeleton exactly: skin `LWF_Attendee_Rig`, the same 17 bone names, the same
node layout. Its animation tracks therefore address the same paths as the rig's own clips.

**Load once per sex** and add the clips to each rigged guest's `AnimationPlayer` in `RiggedBody()`:

```csharp
var lib = GD.Load<PackedScene>($"res://assets/characters/lwf_attendee_{sex}_rain_clips_v1.glb").Instantiate<Node3D>();
var src = lib.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().First();
var rain = src.GetAnimationLibrary("");     // rain_hunched, rain_huddle, rain_hands_head, rain_hurry
lib.QueueFree();
// per guest body: player.GetAnimationLibrary("").AddAnimation(name, rain.GetAnimation(name)) for each, or AddAnimationLibrary("rain", rain)
```

- Add the four names to `LoopedClips`.
- If they're added as a library named `"rain"`, the clips are `"rain/rain_hunched"` and so on.
- **Check the track root:** the glTF importer roots tracks at the `AnimationPlayer`'s parent, so the library's tracks
  (`Skeleton3D:Hips` etc.) line up when both scenes import with the same root. If a track path differs, re-target by
  replacing the prefix once at load.

**When to play which** (the board's rules):

| Situation | Clip |
|---|---|
| Out in rain, standing or queueing, no poncho | `rain_hunched` |
| Out in rain, walking, no poncho | `rain_hands_head` (about half of guests) or `rain_hurry` (the rest; use it whenever they're heading to cover). Pick per guest from the seed so it's stable. |
| Under cover in a huddle (a marquee shelter rect, the bar canopy, the van counter, the first aid porch) while it rains | `rain_huddle` |
| Poncho wearer | normal clips |
| Holding food or a drink | normal `*_food`/`*_carry`/`drink` clips, so the props stay in hand |

**Speeds:** `rain_hurry` covers ground exactly like `walk_hurry`, so use `HurrySpeedAt1x`. `rain_hands_head` matches
`walk`, so use `WalkSpeedAt1x(sex)`.

**Blending:** cross-fade 0.25 s into and out of the rain clips (`player.Play(name, 0.25)`) so the arms don't snap.

## 2. Soaked palette

`attendee_palette_soaked_v1_contract.json` has the same schema as the v1 contract, so it reuses the existing code
unchanged:

1. Parse it with `AttendeePalette.Parse(...)` alongside `GuestPaletteContract`.
2. In `GuestPaletteMaterial`, build a second set of 16 variants from the soaked contract, cached under the same key with
   a `":soaked"` suffix.
3. In `ApplyGuestPalette`, pick the soaked variant when the guest's `soaked` flag is set. The guest keeps their own
   colourway, so a teal shirt soaks to dark teal.
4. Optionally set the soaked variant's `Roughness` to 0.42 (the original is matte) for a faint sheen.

**The soaked flag** (suggested timing):
- **Soaking:** set after about 20 s in rain without cover or a poncho.
- **Drying:** clear after about 120 s under cover, or 60 s in a dry spell.
- **Poncho wearers:** never soak.

Only rigged guests need it. The static pose bodies use the same palette materials, so they can follow the same rule
for free.

## 3. Clear poncho

`lwf_attendee_<sex>_poncho_clear_v1.glb` holds one skinned mesh, `LWF_Poncho_Clear`, bound to the same 17 joints.
Attach it to a rigged guest the way a skinned garment shares a skeleton:

1. Instance the GLB, find its `MeshInstance3D` (`LWF_Poncho_Clear`), and reparent it under the guest's `Skeleton3D`.
2. Set `mesh.Skeleton = ".."` (the guest's skeleton). The mesh's `Skin` binds by bone name, which is identical. Free the
   rest of the instanced scene.
3. Leave its materials as imported. The clear PVC is `Transparency = Alpha`; the white hem trim is opaque.
4. **Sorting:** one transparent surface per guest over the opaque body is fine. Godot sorts per-object by distance, and
   at this camera ponchos rarely overlap.

**Rules:**
- **Who wears one:** guests who bought a poncho at the bar, while it rains or until they leave.
- **Mood:** wearers ignore the rain mood penalty and don't soak.
- **Clips:** they use their normal clips. The hood is part of the mesh, so no hair or hat change is needed.
- **Hats:** hide any hat piece under the hood (flower crown etc.) while it's worn.

## 4. Two body types

Every file comes in `male` and `female` versions, built on each sex's own rig. Use the guest's sex suffix as
`RiggedFile()` does.
