# review.py -- <kit.glb> <sex> <out.png> <frames comma> [mic]
import bpy, sys, math
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]
KIT, SEX, OUTP, FRAMES = a[0], a[1], a[2], [int(x) for x in a[3].split(",")]
MIC = len(a) > 4 and a[4] == "mic"
CH = "C:/Projects/festival-tycoon/game/assets/characters/"
ENV = "C:/Projects/festival-tycoon/game/assets/environment/"
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
def imp(p):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=p); return [o for o in bpy.data.objects if o not in before]
body = imp(CH + f"lwf_performer_{SEX}_body_v2.glb")
for o in body:
    if "Idle" in o.name: bpy.data.objects.remove(o)
kit = imp(KIT)
if MIC:
    for o in imp(ENV + "lwf_mic_stand_v1.glb"):
        if o.name.startswith("MicHead"): o.location.z = {"male": 1.50, "female": 1.43}[SEX]
# merge all actions into the NLA so every object plays together
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16
sc.render.resolution_x, sc.render.resolution_y = 520, 620
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.78, 0.82, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.9
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.0; sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.15
VIEWS = [(20, 8), (75, 10), (-60, 10), (180+25, 15)]   # yaw from front (deg), elevation
import os
tiles = []
for fr in FRAMES:
    sc.frame_set(fr)
    for yaw, el in VIEWS:
        t = Vector((0, 0.05, 1.18))
        d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(el)), math.cos(math.radians(yaw)) * math.cos(math.radians(el)), math.sin(math.radians(el))))
        cam.location = t + d * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        p = OUTP.replace(".png", f"_{fr}_{yaw}.png"); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); tiles.append(p)
# montage rows=frames
import numpy as np
imgs = []
for p in tiles:
    im = bpy.data.images.load(p); W, H = im.size; arr = np.array(im.pixels[:]).reshape(H, W, 4); imgs.append(arr); bpy.data.images.remove(im); os.remove(p)
rows = [np.concatenate(imgs[i * len(VIEWS):(i + 1) * len(VIEWS)], axis=1) for i in range(len(FRAMES))]
full = np.concatenate(rows[::-1], axis=0)
H, W = full.shape[:2]
out = bpy.data.images.new("m", W, H); out.pixels = full.ravel().tolist(); out.filepath_raw = OUTP; out.file_format = 'PNG'; out.save()
print("SAVED", OUTP)
