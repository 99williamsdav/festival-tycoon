# blender -b --python build_siteplan_arm.py
# -> out/lwf_fp_arm_right_siteplan_v1.glb : the RIGHT hand holding Control's laminated site plan on a landscape clipboard
#    (approved site map pick B1, raised pose). Camera space, like the other fp arms: child of the eye camera at identity.
#    Nodes: LWF_FPArm_Right_SitePlan (forearm, wristband, hand, board, clip)  LWF_FPPlan_Sheet (the laminated sheet quad, UV 0..1,
#    texture 1400 x 1276 = the site plan; the game swaps the albedo for its plan SubViewport's ViewportTexture).
import bpy, bmesh, os, sys, json, hashlib, math
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out")
bpy.ops.wm.read_factory_settings(use_empty=True)
g = dict(__name__="fp_kit"); exec(open(os.path.join(HERE, "fp_kit.py"), encoding="utf-8").read(), g)
box, rot = g["box"], g["rot"]
before = set(bpy.data.objects)
C = Vector((0.11, -0.025, -0.40)); R = rot('Y', -6) @ rot('X', -12) @ rot('Z', -3)          # board centre and attitude (camera space)
ASP = 1276 / 1400; BW = 0.42; SW = 0.39; SH = SW * ASP; BH = SH + 0.045
box(C + R @ Vector((0, -0.008, -0.006)), (BW, BH, 0.008), ("board", "8a6a45"), R, 0.004)
box(C + R @ Vector((0, BH / 2 - 0.012, 0.004)), (0.11, 0.028, 0.012), ("steel", "b9bdc2"), R, 0.004)
grip = C + R @ Vector((BW / 2 - 0.012, -0.06, 0)); skin = g["SKIN"]
box(grip + R @ Vector((0.012, 0, -0.022)), (0.05, 0.085, 0.034), skin, R, 0.008)
box(grip + R @ Vector((-0.004, 0.006, 0.012)), (0.05, 0.022, 0.016), skin, R @ rot('Z', 12), 0.005)
wrist = grip + R @ Vector((0.05, -0.03, -0.03))
g["sleeve"](Vector((0.46, -0.52, -0.12)), wrist, "A", "R")
box(wrist + (grip - wrist) * 0.45, (0.06, 0.07, 0.05), skin, R, 0.008)
parts = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
for m in bpy.data.materials:
    m.name = {"skin": "LWF_FP_Skin", "hivis": "LWF_FP_HiVis", "silver": "LWF_FP_Reflective", "board": "LWF_FPPlan_Board", "steel": "LWF_FPPlan_Clip"}.get(m.name, m.name)
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join(); arm = bpy.context.view_layer.objects.active
arm.name = arm.data.name = "LWF_FPArm_Right_SitePlan"
# the sheet: one quad, 1.5 mm above the board face, UV (0,0) bottom-left .. (1,1) top-right
hw, hh = SW / 2, SH / 2; zf = -0.0015
me = bpy.data.meshes.new("LWF_FPPlan_Sheet")
me.from_pydata([C + R @ Vector(p) for p in ((-hw, -hh - 0.02, zf), (hw, -hh - 0.02, zf), (hw, hh - 0.02, zf), (-hw, hh - 0.02, zf))], [], [(0, 1, 2, 3)])
uv = me.uv_layers.new(name="UVMap")
for i, c in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))): uv.data[i].uv = c
mt = bpy.data.materials.new("LWF_FPPlan_Sheet"); mt.use_nodes = True; b = mt.node_tree.nodes["Principled BSDF"]
tx = mt.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(HERE, "siteplan_default_sheet.png"))
mt.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.22; me.materials.append(mt)
sheet = bpy.data.objects.new("LWF_FPPlan_Sheet", me); bpy.context.scene.collection.objects.link(sheet)
for o in (arm, sheet):
    for p in o.data.polygons: p.use_smooth = False
    o.data.transform(Matrix.Rotation(math.radians(90), 4, 'X')); o.data.update()            # kit is Y-up camera space -> glTF Z-up bake
root = bpy.data.objects.new("LWF_FPArm_Right_SitePlan_Root", None); bpy.context.scene.collection.objects.link(root)
arm.parent = root; sheet.parent = root
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); arm.select_set(True); sheet.select_set(True)
path = os.path.join(OUT, "lwf_fp_arm_right_siteplan_v1.glb")
bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
sc = sum((sheet.matrix_world @ v.co for v in sheet.data.vertices), Vector()) / 4
rep = json.load(open(os.path.join(OUT, "fp_arms_report.json")))
rep["lwf_fp_arm_right_siteplan_v1.glb"] = dict(root="LWF_FPArm_Right_SitePlan_Root", nodes={o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (arm, sheet)},
    sheet_size_m=[SW, round(SH, 4)], sheet_texture_px=[1400, 1276], sheet_centre_camera_space=[round(sc.x, 4), round(sc.z, 4), round(-sc.y, 4)],
    place="child of the eye camera at identity", sha256=hashlib.sha256(open(path, "rb").read()).hexdigest())
json.dump(rep, open(os.path.join(OUT, "fp_arms_report.json"), "w"), indent=1)
print("EXPORTED", rep["lwf_fp_arm_right_siteplan_v1.glb"]["nodes"], rep["lwf_fp_arm_right_siteplan_v1.glb"]["sheet_centre_camera_space"])
