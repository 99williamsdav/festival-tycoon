import bpy, math, sys, bmesh, os, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; MODE, OUTD = a[0], a[1]; os.makedirs(OUTD, exist_ok=True)
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
CH = "C:/Projects/festival-tycoon/game/assets/characters/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.83, 0.88, 0.76, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.8; sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(30))
g = bpy.data.objects.new("ground", bpy.data.meshes.new("g")); sc.collection.objects.link(g)
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); bm.to_mesh(g.data); bm.free()
gm = bpy.data.materials.new("grass"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.21, 0.38, 0.08, 1); g.data.materials.append(gm)
lm = bpy.data.materials.new("line"); lm.use_nodes = True; lm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.15, 0.30, 0.06, 1)
for i in range(-30, 31):
    for axis in (0, 1):
        bm = bmesh.new(); s = (60, 0.012, 0.002) if axis == 0 else (0.012, 60, 0.002)
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0 if axis == 0 else i * 0.5, i * 0.5 if axis == 0 else 0, 0.001)) @ Matrix.Diagonal((*s, 1)))
        me = bpy.data.meshes.new("l"); bm.to_mesh(me); bm.free(); me.materials.append(lm); o = bpy.data.objects.new("l", me); sc.collection.objects.link(o)
img_cache = {}
def role_paint(body, role, hair):
    m = body.data.materials[0].copy(); n = [x for x in m.node_tree.nodes if x.type == 'TEX_IMAGE'][0]
    im = n.image.copy(); px = list(im.pixels); Wd, Hd = im.size
    rp = bpy.data.images.load(CH + f"lwf_{role}_body_palette_v1.png"); rpx = list(rp.pixels); RW = rp.size[0]
    for y in range(Hd):
        for x in range(3 * 8, 9 * 8):
            j = (y * Wd + x) * 4; k = (y * RW + x) * 4; px[j:j + 3] = rpx[k:k + 3]
    hx = ["3F2F28", "292725", "9A8056", "79503A"][hair]; rgb = [int(hx[q:q + 2], 16) / 255 for q in (0, 2, 4)]
    for y in range(Hd):
        for x in range(80, 88): j = (y * Wd + x) * 4; px[j:j + 3] = rgb
    im.pixels = px; n.image = im; n.interpolation = 'Closest'; body.data.materials[0] = m
    return m
USED = set(); walkers = []
ROLES = ("medic", "steward", "maintenance", "sound")
for i, role in enumerate(ROLES):
    sex = "male" if i % 2 == 0 else "female"
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=O + f"lwf_{role}_{sex}_rigged_test_v1.glb")
    new = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in new if o.type == 'ARMATURE'); body = next(o for o in new if o.name.startswith("LWF_Attendee_Body"))
    names = [n for n in bpy.data.actions.keys() if n.startswith("walk_hurry_LWF") and n not in USED]; USED.add(names[-1])
    rig.animation_data.action = bpy.data.actions[names[-1]]
    for t in rig.animation_data.nla_tracks: t.mute = True
    hb = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=CH + f"lwf_hair_{sex}_default_v1.glb")
    hair = [o for o in bpy.data.objects if o not in hb]
    for h in hair:
        mw = h.matrix_world.copy(); h.parent = rig; h.parent_type = 'BONE'; h.parent_bone = "Head"; bpy.context.view_layer.update(); h.matrix_world = mw
    mat = role_paint(body, role, i % 4)
    for h in hair:
        if h.type == 'MESH': h.data.materials[0] = mat
    rig.rotation_mode = 'XYZ'; yaw = -135 if i % 2 == 0 else -45
    rig.rotation_euler = (0, 0, math.radians(yaw))
    js = json.load(open(O + f"lwf_{role}_{sex}_rigged_test_v1.json"))["stride_hurry"]
    walkers.append((rig, Vector(((i - 1.5) * 1.3, (i - 1.5) * 1.3, 0)), js["walk_speed_m_per_s"], js["frames"] - 1))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
d = Vector((50.9, -50.9, 58)).normalized()
if MODE == "close": sc.render.resolution_x, sc.render.resolution_y = 1000, 560; cam.data.ortho_scale = 8.5; frames = range(1, 33)
else:
    Z = float(MODE[4:]); sc.render.resolution_x, sc.render.resolution_y = 320, 200; cam.data.ortho_scale = 320 / (900 / Z); frames = range(1, 33)
OFFSET = None
for f in frames:
    t = (f - 1) / 24
    for r, start, v, n in walkers:
        r.location = start + (Matrix.Rotation(r.rotation_euler.z, 3, 'Z') @ Vector((0, 1, 0))) * v * t
    sc.frame_set(((f - 1) % walkers[0][3]) + 1)
    cen = sum((r.location for r, _, _, _ in walkers), Vector()) / len(walkers) + Vector((0, 0, 0.85))
    cam.location = cen + d * 40; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler(); cam.data.clip_end = 100
    sc.render.filepath = os.path.join(OUTD, f"{MODE}_{f:03d}.png"); bpy.ops.render.render(write_still=True)
