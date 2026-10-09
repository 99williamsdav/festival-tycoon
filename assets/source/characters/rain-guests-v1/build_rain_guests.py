# blender -b --python build_rain_guests.py -- <male|female>
# Approved heavy-rain board: soaked guests (four poses) and the clear poncho (option A).
# -> out/lwf_attendee_<sex>_rain_clips_v1.glb     the rig (LWF_Attendee_Rig, same 17 bones) carrying four looping clips:
#        rain_hunched     idle base, hunched, arms wrapped round the chest, a small shiver       49 frames (1..49)
#        rain_huddle      idle base, arms folded, a little hunched, a slow sway                  49 frames
#        rain_hands_head  walk base, hands clasped on the head                                   21 frames (same stride as walk)
#        rain_hurry       walk_hurry base, hunched, arms tucked in and pumping                    16 frames (same stride as walk_hurry)
#    out/lwf_attendee_<sex>_poncho_clear_v1.glb   the rig + LWF_Poncho_Clear: a hooded clear PVC drape skinned to the same bones
#    out/rain_guests_<sex>_report.json
# Each new clip is the existing clip re-evaluated frame by frame, with the upper body overridden, so legs, hips, timing and stride
# are exactly the originals.
import bpy, sys, os, json, math, hashlib
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
SEX = sys.argv[sys.argv.index("--") + 1]
exec(open(os.path.join(HERE, "rain_kit.py"), encoding="utf-8").read())
RIG = f"C:/Projects/festival-tycoon/game/assets/characters/lwf_attendee_{SEX}_rigged_test_v1.glb"
RIGDIR = "C:/Projects/festival-tycoon/game/assets/characters/"                   # installed rig, not the source copy
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene; sc.render.fps = 24
g = Guest(SEX, ci=0, hi=0, wet=False, name="root")
rig = g.rig
base = {a.name.replace("_LWF_Attendee_Rig", ""): a for a in bpy.data.actions}
s = lambda side: -1 if side == "Left" else 1


def override(kind, f, n):
    ph = 2 * math.pi * (f - 1) / (n - 1)                                          # loops: frame 1 == frame n
    if kind == "rain_hunched":
        g.spine(0.42, 0.65 + 0.03 * math.sin(ph * 2), 0.85)
        g.aim("Chest", (0.025 * math.sin(ph * 8), 0.42, 1))                        # the shiver: 8 small side-to-side shakes per loop
        for side in ("Left", "Right"):
            sh = g.head_of(f"{side}UpperArm")
            g.arm_to(side, sh + Vector((-s(side) * 0.21, 0.19, -0.09)), (s(side) * 0.7, -0.2, -0.6), (-s(side) * 1.0, 0.1, 0.25))
    elif kind == "rain_huddle":
        g.spine(0.15, 0.3, 0.35); g.aim("Chest", (0.03 * math.sin(ph), 0.15, 1))    # slow sway
        for side in ("Left", "Right"):
            sh = g.head_of(f"{side}UpperArm")
            g.arm_to(side, sh + Vector((-s(side) * 0.19, 0.17, -0.17)), (s(side) * 0.6, -0.4, -0.6), (-s(side) * 1.0, 0.1, 0.2))
    elif kind == "rain_hands_head":
        g.spine(0.12, 0.25)
        top = g.head_top_posed()
        for side in ("Left", "Right"):
            g.arm_to(side, top + Vector((s(side) * 0.085, 0.0, 0.035)), (s(side) * 1.0, 0.2, 0.15), (-s(side) * 1.0, 0.1, -0.12))
    elif kind == "rain_hurry":
        g.spine(0.38, 0.6, 0.8)
        for side, k in (("Left", 1), ("Right", -1)):
            sh = g.head_of(f"{side}UpperArm"); sw = 0.07 * math.sin(ph) * k                                  # arms pump with the stride
            g.arm_to(side, sh + Vector((-s(side) * 0.05, 0.13 + sw, -0.30)), (s(side) * 0.3, -1, 0), (-s(side) * 0.3, 1, 0.5))


CLIPS = (("rain_hunched", "idle"), ("rain_huddle", "idle"), ("rain_hands_head", "walk"), ("rain_hurry", "walk_hurry"))
baked = {}
for name, src in CLIPS:
    A = base[src]; f0, f1 = int(A.frame_range[0]), int(A.frame_range[1]); n = f1 - f0 + 1
    rig.animation_data_create(); rig.animation_data.action = A; frames = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f); g.upd(); override(name, f - f0 + 1, n); g.upd()
        frames.append({pb.name: (pb.location.copy(), pb.rotation_quaternion.copy() if pb.rotation_mode == 'QUATERNION' else pb.matrix_basis.to_quaternion(),
                                 pb.matrix_basis.to_quaternion()) for pb in rig.pose.bones})
    baked[name] = (f0, frames)
rig.animation_data.action = None
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
for name, (f0, frames) in baked.items():
    act = bpy.data.actions.new(name); act.use_fake_user = True
    for bname in frames[0]:
        dp = f'pose.bones["{bname}"]'
        for idx in range(4):
            fc = act.fcurves.new(dp + ".rotation_quaternion", index=idx, action_group=bname)
            prev = None
            for i, fr in enumerate(frames):
                q = fr[bname][2]
                if prev is not None and prev.dot(q) < 0: q = -q                          # keep quaternions continuous
                fc.keyframe_points.insert(f0 + i, q[idx]); prev = q if idx == 3 else prev
        for idx in range(3):
            fc = act.fcurves.new(dp + ".location", index=idx, action_group=bname)
            for i, fr in enumerate(frames): fc.keyframe_points.insert(f0 + i, fr[bname][0][idx])
    tr = rig.animation_data.nla_tracks.new(); tr.name = name; tr.strips.new(name, f0, act); tr.mute = True
for pb in rig.pose.bones: pb.matrix_basis = Matrix()
g.upd()

REPORT = {}
def export(objs, fname, anims):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    path = os.path.join(OUT, fname)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=anims,
                              export_animation_mode='NLA_TRACKS' if anims else 'ACTIONS', export_skins=True, export_def_bones=False)
    REPORT[fname] = dict(sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), nodes=[o.name for o in objs])
    print("EXPORTED", fname)

# clip library: the armature alone (no mesh), named exactly as in the rig GLB so the track paths match
body_objs = [o for o in g.objs if o.type != 'ARMATURE']
export([rig], f"lwf_attendee_{SEX}_rain_clips_v1.glb", True)
REPORT[f"lwf_attendee_{SEX}_rain_clips_v1.glb"]["clips"] = {k: dict(frames=len(v[1]), base=dict(CLIPS)[k]) for k, v in baked.items()}

# clear poncho: built on the rest body, skinned with automatic weights, trimmed with an opaque white hem so it reads at zoom
for tr in rig.animation_data.nla_tracks: tr.mute = True
pon = make_poncho(g, "clear", name="LWF_Poncho_Clear")
m = pon.data.materials[0]; m.name = "LWF_Poncho_Clear"
bp = m.node_tree.nodes["Principled BSDF"]; bp.inputs["Base Color"].default_value = _lin("dfeef5"); bp.inputs["Alpha"].default_value = 0.35
bp.inputs["Roughness"].default_value = 0.08; m.blend_method = 'BLEND'; m.use_backface_culling = False
trim = bpy.data.materials.new("LWF_Poncho_Trim"); trim.use_nodes = True; tb = trim.node_tree.nodes["Principled BSDF"]
tb.inputs["Base Color"].default_value = _lin("f4f6f7"); tb.inputs["Roughness"].default_value = 0.4; trim.use_backface_culling = False
pon.data.materials.append(trim)
zs = [v.co.z for v in pon.data.vertices]; zmin = min(zs)
for p in pon.data.polygons:                                                       # the bottom ring of the drape and the hood's front edge
    vz = [pon.data.vertices[i].co.z for i in p.vertices]
    if max(vz) < zmin + 0.03: p.material_index = 1
bpy.context.view_layer.objects.active = pon
for md in list(pon.modifiers):
    if md.type == 'SOLIDIFY': bpy.ops.object.modifier_apply(modifier=md.name)
for o in body_objs: o.hide_set(True)
export([rig, pon], f"lwf_attendee_{SEX}_poncho_clear_v1.glb", False)
me = pon.data; me.calc_loop_triangles()
REPORT[f"lwf_attendee_{SEX}_poncho_clear_v1.glb"].update(triangles=len(me.loop_triangles), materials=[x.name for x in me.materials],
                                                          vertex_groups=sorted({pon.vertex_groups[gg.group].name for v in me.vertices for gg in v.groups if gg.weight > 0.01}))
json.dump(REPORT, open(os.path.join(OUT, f"rain_guests_{SEX}_report.json"), "w"), indent=1)
