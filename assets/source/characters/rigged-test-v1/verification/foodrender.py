"""Renders the food clips (walk_food, idle_food, eat) with the chip tray on LWF_RightHand_Food, from the front three-quarter
and the side, into one sheet: blender -b --python foodrender.py -- <male|female> <out.png>"""
import bpy, math, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; SEX, OUTP = a[0], a[1]
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
TRAY = "C:/Projects/festival-tycoon/game/assets/props/lwf_chips_tray_v1.glb"

bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x, sc.render.resolution_y = 300, 360; sc.render.film_transparent = False
world = bpy.data.worlds.new("w"); sc.world = world; world.color = (0.55, 0.62, 0.5)
bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{SEX}_rigged_test_v1.glb")
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
sock = next(o for o in bpy.data.objects if o.name.startswith("LWF_RightHand_Food"))
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=TRAY)
for o in [o for o in bpy.data.objects if o not in before and o.parent is None]:
    o.parent = sock; o.matrix_parent_inverse.identity(); o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
for t in rig.animation_data.nla_tracks: t.mute = True
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.0


def look(azimuth, elevation=12):
    r = 6; az = math.radians(azimuth); el = math.radians(elevation)
    target = Vector((0, 0, 0.95))
    cam.location = target + Vector((r * math.sin(az) * math.cos(el), -r * math.cos(az) * math.cos(el), r * math.sin(el)))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()


shots = [("idle_food", 1), ("walk_food", 1), ("walk_food", 11), ("eat", 1), ("eat", 30), ("walk_brisk_food", 5)]
tiles = []
for view, az in (("front", 215), ("side", 270)):
    look(az)
    for act, fr in shots:
        rig.animation_data.action = bpy.data.actions[next(n for n in bpy.data.actions.keys() if n == act or n.startswith(act + "_"))]
        sc.frame_set(fr); bpy.context.view_layer.update()
        p = OUTP.replace(".png", f"_{view}_{act}_{fr}.png"); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); tiles.append(p)
print("TILES", ";".join(tiles))
