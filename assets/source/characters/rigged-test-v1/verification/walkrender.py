import bpy, math, sys, bmesh, os, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; MODE, OUTD = a[0], a[1]; os.makedirs(OUTD, exist_ok=True)
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.83, 0.88, 0.76, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.8; sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(30))
g = bpy.data.objects.new("ground", bpy.data.meshes.new("g")); sc.collection.objects.link(g)
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=12, y_segments=12, size=40); bm.to_mesh(g.data); bm.free()
gm = bpy.data.materials.new("grass"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.21, 0.38, 0.08, 1); g.data.materials.append(gm)
# grid lines so foot sliding is visible
lm = bpy.data.materials.new("line"); lm.use_nodes = True; lm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.15, 0.30, 0.06, 1)
for i in range(-20, 21):
    for axis in (0, 1):
        bm = bmesh.new(); s = (40, 0.012, 0.002) if axis == 0 else (0.012, 40, 0.002)
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0 if axis == 0 else i * 0.5, i * 0.5 if axis == 0 else 0, 0.001)) @ Matrix.Diagonal((*s, 1)))
        me = bpy.data.meshes.new("l"); bm.to_mesh(me); bm.free(); me.materials.append(lm); o = bpy.data.objects.new("l", me); sc.collection.objects.link(o)
SPEED = {s: json.load(open(O + f"lwf_attendee_{s}_rigged_test_v1.json"))["stride"]["walk_speed_m_per_s"] for s in ("male", "female")}
def guest(sex, act, cloth, hair):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{sex}_rigged_test_v1.glb")
    rig = next(o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE')
    body = next(o for o in bpy.data.objects if o not in before and o.type == 'MESH')
    act_names = [n for n in bpy.data.actions.keys() if n.startswith(act + "_LWF") and n not in USED]
    USED.add(act_names[-1]); rig.animation_data.action = bpy.data.actions[act_names[-1]]
    for t in rig.animation_data.nla_tracks: t.mute = True
    hb = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=f"C:/Projects/festival-tycoon/game/assets/characters/lwf_hair_{sex}_default_v1.glb")
    for h in [o for o in bpy.data.objects if o not in hb]:
        mw = h.matrix_world.copy(); h.parent = rig; h.parent_type = 'BONE'; h.parent_bone = "Head"; bpy.context.view_layer.update(); h.matrix_world = mw
    # recolour body + hair with a contract colourway
    C = json.load(open("C:/Projects/festival-tycoon/game/assets/characters/attendee_palette_v1_contract.json"))
    slots = dict(C["clothing_colourways"][cloth]["slots"]); slots.update(C["hair_colours"][hair]["slots"])
    for o in {body, *[c for c in rig.children_recursive if c.type == 'MESH']}:
        if not o.data.materials or not any(x.type == 'TEX_IMAGE' for x in o.data.materials[0].node_tree.nodes): continue
        m = o.data.materials[0].copy(); n = [x for x in m.node_tree.nodes if x.type == 'TEX_IMAGE'][0]; im = n.image.copy(); px = list(im.pixels); Wd = im.size[0]
        for sl, hx in slots.items():
            rgb = [int(hx[q:q + 2], 16) / 255 for q in (0, 2, 4)]
            for y in range(im.size[1]):
                for x in range(8 * int(sl), 8 * int(sl) + 8): j = (y * Wd + x) * 4; px[j:j + 3] = rgb
        im.pixels = px; n.image = im; n.interpolation = 'Closest'; o.data.materials[0] = m
    rig.rotation_mode = 'XYZ'
    if PROP:
        sock = next(o for o in bpy.data.objects if o not in before and o.name.startswith("LWF_RightHand_Cup"))
        pb_ = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath="C:/Projects/festival-tycoon/assets/runtime/props/" + PROP)
        for o in [o for o in bpy.data.objects if o not in pb_ and o.parent is None]:
            o.parent = sock; o.matrix_parent_inverse = Matrix(); o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
    return rig
USED = set()
PROP = None
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
d = Vector((50.9, -50.9, 58)).normalized()
walkers = []
if MODE.startswith("brisk"):
    BRISK_SPEED = {s: json.load(open(O + f"lwf_attendee_{s}_rigged_test_v1.json"))["stride_brisk"]["walk_speed_m_per_s"] for s in ("male", "female")}
    SPEED.update(BRISK_SPEED); ACTN = "walk_brisk"
    for i, (sex, yaw) in enumerate((("male", -135), ("female", -45), ("male", 45), ("female", -135))):
        r = guest(sex, ACTN, i % 4, (i * 3) % 4); start = Vector(((i - 1.5) * 1.15, (i - 1.5) * 1.15, 0))
        r.rotation_euler = (0, 0, math.radians(yaw)); walkers.append((r, start, sex))
    if MODE == "brisk":
        sc.render.resolution_x, sc.render.resolution_y = 900, 520; cam.data.ortho_scale = 7.5; frames = range(1, 21)
    else:
        Z = float(MODE[10:]); sc.render.resolution_x, sc.render.resolution_y = 320, 200; cam.data.ortho_scale = 320 / (900 / Z); frames = range(1, 41)
elif MODE in ("carry", "drink"):
    PROP = "lwf_beer_cup_v2.glb"
    act = "walk_carry" if MODE == "carry" else "drink"
    for i, (sex, yaw) in enumerate((("male", -135), ("female", -45), ("male", 45), ("female", -135))):
        if MODE == "drink": PROP = "lwf_beer_cup_v2.glb" if i % 2 == 0 else "lwf_soft_drink_cup_v1.glb"
        r = guest(sex, act if not (MODE == "drink" and i % 2) else "drink_soft", i % 4, (i * 3) % 4)
        start = Vector(((i - 1.5) * 1.15, (i - 1.5) * 1.15, 0)); r.rotation_euler = (0, 0, math.radians(yaw if MODE == "carry" else -135 + i * 30))
        walkers.append((r, start, sex))
    sc.render.resolution_x, sc.render.resolution_y = 900, 520; cam.data.ortho_scale = 6.0 if MODE == "drink" else 7.5
    frames = range(1, 28, 2) if MODE == "carry" else range(1, 67, 3)
elif MODE == "sheet":
    for i, (sex, yaw) in enumerate((("male", -135), ("female", -45), ("male", 45), ("female", -135))):
        r = guest(sex, "walk", i % 4, (i * 3) % 4); start = Vector(((i - 1.5) * 1.15, (i - 1.5) * 1.15, 0))
        r.rotation_euler = (0, 0, math.radians(yaw)); walkers.append((r, start, sex))
    sc.render.resolution_x, sc.render.resolution_y = 900, 520; cam.data.ortho_scale = 7.5; frames = range(1, 28, 2)
else:  # zoom crowd at true game pixels
    import random; rng = random.Random(4)
    for i in range(10):
        sex = "male" if i % 2 == 0 else "female"; yaw = rng.choice((-135, -45, 45, 135, -90, 0))
        r = guest(sex, "walk", rng.randrange(4), rng.randrange(4)); start = Vector((rng.uniform(-4, 4), rng.uniform(-3, 3), 0))
        r.rotation_euler = (0, 0, math.radians(yaw)); walkers.append((r, start, sex))
    Z = float(MODE[4:]); ppm = 900 / Z
    sc.render.resolution_x, sc.render.resolution_y = 320, 200; cam.data.ortho_scale = 320 / ppm; frames = range(1, 55)
for f in frames:
    sc.frame_set((((f - 1) % (20 if MODE.startswith("brisk") else 27)) + 1) if MODE != "drink" else f)
    t = (f - 1) / 24 if MODE != "drink" else 0.0
    for r, start, sex in walkers:
        fwd = Matrix.Rotation(r.rotation_euler.z, 3, 'Z') @ Vector((0, 1, 0))
        r.location = start + fwd * SPEED[sex] * t
    cen = Vector((0, 0.4, 0.85)) + (Vector((0, SPEED["male"] * t * 0.5, 0)) if MODE == "sheet" else Vector())
    cam.location = cen + d * 40; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler(); cam.data.clip_end = 100
    if f == frames[0]:
        from bpy_extras.object_utils import world_to_camera_view
        bpy.context.view_layer.update()
        pts = [world_to_camera_view(sc, cam, r.location + Vector((0, 0, z))) for r, _, _ in walkers for z in (0, 1.7)]
        mx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2 - 0.5; my = (min(p.y for p in pts) + max(p.y for p in pts)) / 2 - 0.5
        R = cam.matrix_world.to_3x3(); aspect = sc.render.resolution_y / sc.render.resolution_x
        OFFSET = R @ Vector((mx * cam.data.ortho_scale, my * cam.data.ortho_scale * aspect, 0))
    cam.location += OFFSET
    sc.render.filepath = os.path.join(OUTD, f"{MODE}_{f:03d}.png"); bpy.ops.render.render(write_still=True)
