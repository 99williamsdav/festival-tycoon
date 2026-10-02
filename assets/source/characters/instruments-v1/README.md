# Band instruments — v1

2 October 2026. Production assets from the approved instruments first pass. Built **from** the approved v2 kits:
the playing arms, strap, origins, node names and animation conventions are the originals, and only the instrument
geometry is new. No new body meshes. Matte low-poly, palette textures with Closest filtering.

Rebuild: `blender -b --python build_instruments.py -- out`, then
`python merge_animations.py out/lwf_guitarist_*_kit_v2.glb out/lwf_electronic_*_desk_kit_v2.glb`
(merges each kit's animations into one animation, "Animation", as the v2 kits have: the game loops every
AnimationPlayer animation with `Play()`, so a kit must carry exactly one). The acoustic kits are byte copies of the
existing v2 guitarist kits; see below. Verification renders and the scene script are in `verification/`.

## Files

Kits and drum hardware are in `game/assets/characters/` (copies in `assets/runtime/characters/`); props are in
`game/assets/environment/` (copies in `assets/runtime/environment/`).

| File | Tris | Notes |
|---|---:|---|
| `lwf_guitarist_{male,female}_acoustic_kit_v2.glb` | 1000 | **Byte copies of `lwf_guitarist_{sex}_kit_v2`**, which already holds an acoustic guitar (`LWF_Guitarist_AcousticGuitar`). Copied under the requested name for Folk. |
| `lwf_guitarist_{male,female}_electric_kit_v2.glb` | 993 / 1017 | Same arms, strap and strum animation; `LWF_Guitarist_ElectricGuitar` (offset double-cut, red body, cream pickguard). For Indie, Punk and other non-folk leads. |
| `lwf_guitarist_{male,female}_flyingv_kit_v2.glb` | 959 / 983 | Same; `LWF_Guitarist_FlyingVGuitar` (black body, white guard). For Metal. |
| `lwf_electronic_{male,female}_desk_kit_v2.glb` | 676 / 700 | `LWF_Desk_LeftPlayingArm` and `LWF_Desk_RightPlayingArm` (the drummer arms without sticks) and one 49-frame 24 fps loop: a left fader nudge and a right knob twiddle. No instrument; the desk is the stage set's. |
| `lwf_drum_hardware_compact_v1.glb` | 358 | v2 hardware minus the left rack tom and the ride: kick, snare (+ stand), right rack tom, crash (+ stand). Same nodes, origin and orientation as `lwf_drum_hardware_only_v2`. For Folk. |
| `lwf_drum_hardware_double_kick_v1.glb` | 627 | v2 hardware with the kick scaled 0.85 and doubled at x = ±0.29 (clear of the cymbal stands). Same nodes, origin and orientation. For Metal. |
| `lwf_keyboardist_{male,female}_kit_v2.glb` | 940 / 964 | Arms (stick-less drummer arms), a keyboard on an X-stand and one key-press loop. See below. |
| `lwf_drum_hardware_electronic_v1.glb` | 496 | v2 geometry with an electronic-pad palette. See below. |
| `lwf_mic_stand_v1.glb` (environment) | 172 | `MicStand` (base, outer tube to 1.10 m) and `MicHead` (inner tube, boom and mic), the latter movable vertically. |

The guitar and desk arms keep the v2 node names and translations (male shoulders ±0.175 at 1.272; female ±0.149
at 1.22). The drummer's v2 arms and animation line up with both drum variants, because the struck rack tom (right)
and the snare under the left stick are kept.

## Mic stand

The stand is authored in the **performer-local frame** used by the kits: origin at the performer's feet, facing
the same way. Place it at the performer's position with the performer's rotation, with no extra offset.
- The stand is 0.50 m in front of the performer: clear of the guitar body (front at about 0.25–0.29), the neck
  (which passes x = 0 at about 0.96 m high) and both hands.
- Set the `MicHead` node's local Y (height) to the mouth: **male 1.50 m, female 1.43 m**, matching the v2 bodies
  (the female's mouth mark is at 1.43; the male's mouth is 0.10 below his eyes at 1.60). The mic capsule then sits
  0.06 m in front of the face. The inner tube always stays inside the outer tube at either height.
- Verified from the front and from behind on the electric (male) and flying-V (female) kits.

## Electronic: a three-piece on the usual marks (no simulation change)

- **Role 0, front-centre mark (96,150):** `lwf_electronic_{sex}_desk_kit_v2` (arms only, fader-nudge and knob-twiddle
  loop) at the laptop and mixing-desk table from `lwf_stage_set_electronic_v1`. The table now stands directly in
  front of that mark: 1.5 × 0.55 m, top at 0.98 m, centred at stage-local (−0.25, 1.2, 0.825), with the performer
  behind it at the mark, facing the audience. It crosses the front walkway; the steps stay clear, and the band can
  still reach the marks through the 0.9 m gap between the drum kit and the table's back edge. The table covers
  about TraversalGrid cells x 97–98, z 149–151, if you want them unwalkable.
- **Role 1, mark (94,146):** `lwf_keyboardist_{sex}_kit_v2`, described below.
- **Drummer, drum mark (93,152):** the usual drummer kit with `lwf_drum_hardware_electronic_v1`.

Head-nod: not possible with the current rig. The head is part of the single `LWF_Performer_BodyCore` mesh, and
kits only carry arms. A nod would need the head split into its own node on the v2 bodies.

## Keyboardist kits — `lwf_keyboardist_{male,female}_kit_v2` (940 / 964 tris)

- Same conventions as the other kits: `LWF_Keyboardist_LeftPlayingArm` and `…RightPlayingArm` (the v2 drummer arms
  without sticks, same translations) and one animation, "Animation" (25 frames at 24 fps, alternating light key
  presses).
- The kit also contains `LWF_Keyboardist_Keyboard`: a 1.0 m keyboard on an X-stand, part of the kit. The keys sit at
  1.135 m, about 0.50 m in front of the performer, measured from each body's own hand positions so the fingertips
  rest on the keys.
- Recolour via `lwf_keyboard_palette` (64×8, 8 slots, u = (8·slot + 4) / 64): 0 keyboard body, 1 white keys,
  2 black keys, 3 stand, 4 accent strip, 5 end cheeks.

## Electronic drum kit — `lwf_drum_hardware_electronic_v1` (496 tris)

The v2 geometry with only a palette recolour: charcoal shells (slots 12/13), black rubber pads (16), a black kick
head (14), rubber cymbal pads (22) and silver stands (15/21). Same nodes, origin and orientation, so the v2
drummer arms line up.

## Per-act instrument colours

**Guitars and bass all use the same slots** in their own 24-slot palettes (`lwf_guitarist_palette` /
`lwf_bassist_palette`, 192×8, u = (8·slot + 4) / 192):
- slot **12**: body back and sides
- slot **13**: body top (front)
- slot **14**: pickguard (electric, flying-V, bass; the acoustic has none)

Other slots: 11 headstock, 15 neck, 16 fret markers, 20 strings and frets, 21 hardware (soundhole on the acoustic).

**Drum hardware** (`lwf_drummer_palette`, same layout; v2, compact and double kick):
- slot **12**: kick, snare and floor shells
- slot **13**: rack-tom shells
- slot **14**: kick front head (cream; a natural spot for a band logo later)
- slot 16: drum heads; 15, 21 and 22: cymbal hardware and cymbals.

Set the body top a touch lighter than the back and sides (about +8% value), as below.

Suggested body and shell colours (top / back-sides):

| Name | Top (13) | Back/sides (12) |
|---|---|---|
| Sunburst (acoustic default) | D68A4E | B9693F |
| Cherry red | C9553A | B8442E |
| Vintage cream | EDE3C8 | D9CDB0 |
| Gloss black | 2A2A2A | 1A1A1A |
| Seafoam | 8CC4B4 | 76AE9E |
| Natural ash | E0CDA4 | C9B68C |
| Teal (bass default) | 54827A | 3D6D69 |
| Mustard | D4A845 | BF9433 |

Pickguards: cream EDE6D6, white E8E6E0, tortoiseshell 6A3A24, black 2A2A2A.
