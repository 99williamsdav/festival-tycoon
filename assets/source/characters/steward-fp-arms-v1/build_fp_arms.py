# blender -b --python build_fp_arms.py
# First-person steward arms, style A (approved: bare forearm with a hi-vis wristband, as the steward is modelled).
# The radio lives in the LEFT hand; the RIGHT hand eases in for actions.
# -> out/lwf_fp_arm_left_radio_v1.glb      LWF_FPArm_Left (forearm, wristband, fist) + LWF_FPRadio (+ LWF_FPRadio_Screen)
#    out/lwf_fp_arm_right_calm_v1.glb      LWF_FPArm_Right_Calm   open palm, "steady now"
#    out/lwf_fp_arm_right_door_v1.glb      LWF_FPArm_Right_Door   reaching straight out for a handle
#    out/lwf_fp_arm_right_litter_v1.glb    LWF_FPArm_Right_Litter reaching down + LWF_FPLitterCup (a crushed cup, hideable)
#    out/fp_arms_report.json
# All geometry is authored in the first-person CAMERA's space (Godot: +X right, +Y up, -Z forward, metres), so each GLB goes on
# as a child of the eye camera at the identity transform. Blender world == Godot camera space here, so no axis juggling.
import bpy, os, sys, json, hashlib, math
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
REPORT = {}


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    g = dict(__name__="fp_kit"); exec(open(os.path.join(HERE, "fp_kit.py"), encoding="utf-8").read(), g); return g


def gather(before):
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


def mirror_join(objs, name):
    """mirror across X (left <-> right), apply, fix normals, join into one flat-shaded mesh"""
    for o in objs:
        o.scale.x = -1
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
    for p in o.data.polygons: p.use_smooth = False
    return o


def name_materials():
    for m in bpy.data.materials:
        if not m.name.startswith("LWF_FP"): m.name = "LWF_FP_" + {"skin": "Skin", "hivis": "HiVis", "silver": "Reflective", "radio": "Radio", "screen": "RadioScreen",
                                                                    "gold": "RadioKnob", "dark": "RadioGrille", "cup": "Cup", "cupband": "CupBand"}.get(m.name, m.name)


# The kit authors in camera space with Blender +Y as up. The glTF exporter assumes Blender Z-up (Godot +Y = Blender +Z,
# Godot -Z = Blender +Y), so bake a +90 degree turn about X into the mesh data before export: (x, y, z) -> (x, -z, y).
TO_ZUP = Matrix.Rotation(math.radians(90), 4, 'X')


def export(objs, root_name, fname, meta):
    for o in objs: o.data.transform(TO_ZUP); o.data.update()
    root = bpy.data.objects.new(root_name, None); bpy.context.scene.collection.objects.link(root)
    for o in objs: o.parent = root
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
    for o in objs: o.select_set(True)
    path = os.path.join(OUT, fname)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}
    REPORT[fname] = dict(root=root_name, nodes=tris, materials=sorted({m.name for o in objs for m in o.data.materials}),
                         sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), **meta)
    print("EXPORTED", fname, tris)


# ---------------------------------------------------------------- the radio arm (left)
g = fresh(); before = set(bpy.data.objects)
# build the arm and fist first, then the radio pieces, so they can be split into separate nodes
before_arm = set(bpy.data.objects)
c = Vector((0.185, -0.115, -0.40)); Rr = g["rot"]('Y', -16) @ g["rot"]('X', 22) @ g["rot"]('Z', 10)
wrist = c + Rr @ Vector((0, -0.085, -0.004)); elbow = Vector((0.43, -0.52, -0.16))
g["sleeve"](elbow, wrist, "A", "R")
hm = g["SKIN"]; box = g["box"]; tube = g["tube"]
box(c + Rr @ Vector((0.002, -0.040, 0.002)), (0.068, 0.060, 0.050), hm, Rr, 0.008)
for k in range(3): box(c + Rr @ Vector((0.004, -0.024 - k * 0.016, 0.026)), (0.062, 0.013, 0.008), hm, Rr, 0.003)
box(c + Rr @ Vector((-0.030, -0.018, 0.020)), (0.018, 0.046, 0.018), hm, Rr @ g["rot"]('Z', -18), 0.004)
arm = gather(before_arm)
before_radio = set(bpy.data.objects)
box(c, (0.052, 0.112, 0.030), g["RADIO"], Rr, 0.006)
for k in range(3): box(c + Rr @ Vector((0, 0.002 - k * 0.009, 0.0155)), (0.030, 0.003, 0.002), g["DARK"], Rr)
tube(c + Rr @ Vector((0.015, 0.056, 0)), c + Rr @ Vector((0.015, 0.11, 0)), 0.0055, 0.0045, g["RADIO"], 6)
tube(c + Rr @ Vector((-0.014, 0.056, 0)), c + Rr @ Vector((-0.014, 0.066, 0)), 0.006, 0.006, g["GOLD"], 8)
box(c + Rr @ Vector((-0.027, 0.004, 0)), (0.004, 0.026, 0.012), g["GOLD"], Rr)
radio = gather(before_radio)
before_screen = set(bpy.data.objects)
box(c + Rr @ Vector((0, 0.030, 0.0155)), (0.034, 0.024, 0.002), g["SCREEN"], Rr)
screen = gather(before_screen)
name_materials()
scr = bpy.data.materials["LWF_FP_RadioScreen"]; scr.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = scr.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
scr.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 0.6
A = mirror_join(arm, "LWF_FPArm_Left"); Rd = mirror_join(radio, "LWF_FPRadio"); Sc = mirror_join(screen, "LWF_FPRadio_Screen")
sc_c = sum((Sc.matrix_world @ v.co for v in Sc.data.vertices), Vector()) / len(Sc.data.vertices)
export([A, Rd, Sc], "LWF_FPArm_LeftRadio", "lwf_fp_arm_left_radio_v1.glb",
       dict(place="child of the eye camera at identity (camera space: +X right, +Y up, -Z forward)", radio_centre=[-0.185, -0.115, -0.40], camera_space="Godot: +X right, +Y up, -Z forward",
            screen_centre=[round(sc_c.x, 4), round(sc_c.y, 4), round(sc_c.z, 4)], screen_size_m=[0.034, 0.024]))

# ---------------------------------------------------------------- the action arms (right)
POSES = {"calm": "LWF_FPArm_Right_Calm", "door": "LWF_FPArm_Right_Door", "litter": "LWF_FPArm_Right_Litter"}
for pose, node in POSES.items():
    g = fresh(); before = set(bpy.data.objects)
    g["left_action"]("A", pose)
    objs = gather(before); name_materials()
    cup = [o for o in objs if any(m.name in ("LWF_FP_Cup", "LWF_FP_CupBand") for m in o.data.materials)]
    armo = [o for o in objs if o not in cup]
    parts = [mirror_join(armo, node)]
    if cup: parts.append(mirror_join(cup, "LWF_FPLitterCup"))
    export(parts, node + "_Root", f"lwf_fp_arm_right_{pose}_v1.glb", dict(place="child of the eye camera at identity", pose=pose))

json.dump(REPORT, open(os.path.join(OUT, "fp_arms_report.json"), "w"), indent=1)
