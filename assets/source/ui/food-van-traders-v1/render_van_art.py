# blender -b --python vanport.py -- <art> <sign> <out.png>   (art "coffee" = the parked coffee-van-v2 GLB)
import bpy, sys, math, os
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; ART, SIGN, OUT = a
GA = "C:/Projects/festival-tycoon/game/assets/environment/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
def gd(x, y, z): return Vector((x, -z, y))
def imp(path, parent, at):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path); new = [o for o in bpy.data.objects if o not in before]
    top = [o for o in new if o.parent is None]
    for o in top: o.parent = parent; o.location = gd(*at)
    return new
root = bpy.data.objects.new("root", None); sc.collection.objects.link(root)
if ART == "coffee":
    imp("C:/Projects/festival-tycoon/assets/source/environment/coffee-van-v2/blender-v2/coffee-van.glb", root, (0, 0, 0))
else:
    asm = bpy.data.objects.new("asm", None); sc.collection.objects.link(asm); asm.parent = root; asm.location = gd(-2.15, 0, 0)
    pal = bpy.data.images.load(GA + f"lwf_food_van_palette_{ART}_v1.png")
    imp(GA + "lwf_food_van_chassis_v1.glb", asm, (0, 0, 0)); fn = imp(GA + "lwf_food_van_fascia_v1.glb", asm, (2.15, 2.49, 1.18))
    fas = [o for o in fn if o.parent is asm][0]
    for o in [o for o in asm.children_recursive if o.type == 'MESH']:
        for i, m in enumerate(o.data.materials):
            if m and m.name.startswith("LWF_FoodVan_MattePalette"):
                m2 = m.copy(); o.data.materials[i] = m2
                for nd in m2.node_tree.nodes:
                    if nd.type == 'TEX_IMAGE': nd.image = pal; nd.interpolation = 'Closest'
    imp(GA + f"lwf_food_van_name_panel_{ART}_v1.glb", fas, (0, 0, 0.056)); imp(GA + f"lwf_food_van_sign_{SIGN}_v1.glb", asm, (2.10, 2.75, 0))
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)
bpy.context.view_layer.update()
pts = [o.matrix_world @ Vector(c) for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (mn + mx) / 2
sun = bpy.data.objects.new("s", bpy.data.lights.new("s", 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); sun.data.energy = 3.2
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.78, 0.8, 1)
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam
d = Vector((-0.62, -1.0, 0.55)).normalized(); cam.location = c + d * 30; cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z) * 1.25
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32; sc.render.film_transparent = True
sc.render.resolution_x, sc.render.resolution_y = 640, 480; sc.view_settings.view_transform = 'Standard'
sc.render.filepath = OUT; bpy.ops.render.render(write_still=True)
