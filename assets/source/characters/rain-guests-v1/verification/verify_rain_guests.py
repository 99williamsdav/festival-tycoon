# blender -b --python verify_rain_guests.py -- <male|female> <out_dir>    then: python make_sheets.py <out_dir>
# Loads the INSTALLED rig (body + its clips), the INSTALLED rain clip library and poncho, and renders contact rows under a
# gameplay-angle camera:
#   rows 1-4: rain_hunched, rain_huddle, rain_hands_head, rain_hurry on the soaked body (clips from the library GLB)
#   rows 5-8: the clear poncho over idle, walk, walk_hurry and drink (the rig's own clips driving the poncho's armature)
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; SEX, OUTD = a[0], a[1]
C = "C:/Projects/festival-tycoon/game/assets/characters/"
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, "..", "rain_kit.py"), encoding="utf-8").read())
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene; sc.render.fps = 24
g = Guest(SEX, ci=1, hi=1, wet=True, name="body"); rig = g.rig
for o in [o for o in bpy.data.objects if o.name.startswith("Icosphere")]: bpy.data.objects.remove(o)
def imp(path):
    before = set(bpy.data.objects); acts = set(bpy.data.actions); bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in [o for o in new if o.name.startswith("Icosphere")]: bpy.data.objects.remove(o); new.remove(o)
    return new, [x for x in bpy.data.actions if x not in acts]
lib_objs, lib_acts = imp(C + f"lwf_attendee_{SEX}_rain_clips_v1.glb")
for o in lib_objs: o.hide_render = True
pon_objs, _ = imp(C + f"lwf_attendee_{SEX}_poncho_clear_v1.glb")
prig = next(o for o in pon_objs if o.type == 'ARMATURE'); pmesh = next(o for o in pon_objs if o.type == 'MESH')
for o in pon_objs: o.hide_render = True
act = lambda n: next(x for x in bpy.data.actions if x.name == n or x.name.startswith(n + "_"))
print("LIB", [x.name for x in lib_acts])
bpy.ops.mesh.primitive_plane_add(size=20); gm = bpy.data.materials.new("gr"); gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.24, 0.09, 1); bpy.context.object.data.materials.append(gm)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(35)); sun.data.energy = 2.2; sun.data.angle = math.radians(20)
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.7, 1)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
yaw = math.radians(225); t = Vector((0, 0, 0.95)); cam.location = t + Vector((math.sin(yaw), math.cos(yaw), 0.806)) * 12
cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.3
g.root.rotation_euler = (0, 0, math.radians(163));
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.render.resolution_x, sc.render.resolution_y = 300, 360
sc.view_settings.view_transform = 'Standard'
ROWS = [("rain_hunched", False), ("rain_huddle", False), ("rain_hands_head", False), ("rain_hurry", False),
        ("idle", True), ("walk", True), ("walk_hurry", True), ("drink", True)]
os.makedirs(OUTD, exist_ok=True); tiles = []
for r, (clip, poncho) in enumerate(ROWS):
    A = act(clip); f0, f1 = int(A.frame_range[0]), int(A.frame_range[1])
    rig.animation_data_create(); rig.animation_data.action = A
    if poncho:
        prig.parent = g.root; prig.matrix_parent_inverse.identity(); prig.location = (0, 0, 0); prig.rotation_euler = (0, 0, 0)
        prig.animation_data_create(); prig.animation_data.action = A; pmesh.hide_render = False
    else: pmesh.hide_render = True
    for c in range(4):
        f = f0 + round((f1 - f0) * c / 4); sc.frame_set(f)
        p = os.path.join(OUTD, f"_{SEX}_{r}_{c}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True); tiles.append((r, c, p))
print("TILES", len(tiles))
