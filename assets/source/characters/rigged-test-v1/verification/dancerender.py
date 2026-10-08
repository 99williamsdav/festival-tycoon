"""Renders the dance clips (walk_food, idle_food, eat) with the chip tray on LWF_RightHand_Food, from the front three-quarter
and the side, into one sheet: blender -b --python foodrender.py -- <male|female> <out.png>"""
import bpy, math, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; SEX, OUTP = a[0], a[1]
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"

bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x, sc.render.resolution_y = 300, 360; sc.render.film_transparent = False
world = bpy.data.worlds.new("w"); sc.world = world; world.color = (0.55, 0.62, 0.5)
bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{SEX}_rigged_test_v1.glb")
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
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


shots = [(c, f) for c in ("dance_sway", "dance_bop", "dance_full") for f in (1, 7, 13, 19)]
tiles = []
for view, az in (("front", 215), ("side", 270)):
    look(az)
    for act, fr in shots:
        rig.animation_data.action = bpy.data.actions[next(n for n in bpy.data.actions.keys() if n == act or n.startswith(act + "_"))]
        sc.frame_set(fr); bpy.context.view_layer.update()
        p = OUTP.replace(".png", f"_{view}_{act}_{fr}.png"); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); tiles.append(p)
print("TILES", ";".join(tiles))
