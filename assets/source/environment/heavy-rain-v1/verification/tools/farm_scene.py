# Rebuilds the Lower Wittering farm from the game's own GLBs and layout code, under the game camera,
# then lights it for one time of day.
# usage: blender -b --python farm_scene.py -- <time> <out.png> [samples] [width height]
# time: midday | cloud | golden | dusk
import bpy, math, sys, random
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
TIME, OUT = argv[0], argv[1]
OPTS = dict(a.split("=", 1) for a in argv[2:] if "=" in a)
POS = [a for a in argv[2:] if "=" not in a]
SAMPLES = int(POS[0]) if len(POS) > 0 else 48
RES = (int(POS[1]), int(POS[2])) if len(POS) > 2 else (1280, 720)
ZOOM = float(OPTS.get("zoom", 47)); WORLD = OPTS.get("world", "0") == "1"; DRESS = OPTS.get("dress", "0") == "1"; FIELD = OPTS.get("field", "0") == "1"; CROWD = float(OPTS.get("crowd", "1")); LIFE = OPTS.get("life", "0") == "1"
A = "C:/Projects/festival-tycoon/game/assets/"

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

def gd(x, y, z):
    """Godot position -> Blender position"""
    return Vector((x, -z, y))

_cols = {}
def asset(path):
    if path in _cols:
        return _cols[path]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=A + path)
    col = bpy.data.collections.new(path.split("/")[-1])
    for o in [o for o in bpy.data.objects if o not in before]:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        col.objects.link(o)
    _cols[path] = col
    return col

def place(path, x, z, yaw_deg=0.0, y=0.0, scale=1.0):
    e = bpy.data.objects.new(path.split("/")[-1], None)
    e.instance_type = 'COLLECTION'; e.instance_collection = asset(path)
    e.location = gd(x, y, z); e.rotation_euler = (0, 0, math.radians(yaw_deg)); e.scale = (scale,) * 3
    sc.collection.objects.link(e)
    return e

def cell(cx, cz):
    return cx * 0.5 - 63.75, cz * 0.5 - 63.75

# ---------------------------------------------------------------- the farm (mirrors Main.cs / FarmScene.cs)
for x in range(-4, 4):
    for z in range(-4, 4):
        place("environment/lwf_field_grass_tile_8m_v1.glb", x * 8, (z + 1) * 8)
for z in range(-3, 5):
    place("environment/lwf_vehicle_track_straight_8x4m_v1.glb", 0, z * 8, 90, y=0.052)
H8 = "environment/lwf_hedge_straight_8m_a_v1.glb"
for i in range(-4, 4):
    place(H8, i * 8, -32)
    if i not in (-1, 0):
        place(H8, i * 8, 32)
    place(H8, -32, i * 8, 90)
    place(H8, 32, i * 8, 90)
place("environment/lwf_hedge_straight_4m_a_v1.glb", -8, 32)
place("environment/lwf_hedge_straight_4m_a_v1.glb", 4, 32)
place("environment/lwf_hedge_gate_end_v1.glb", -3, 32)
place("environment/lwf_hedge_gate_end_v1.glb", 3, 32, 180)
place("environment/lwf_farmhouse_v1.glb", -22, -14)
place("environment/lwf_barn_v2.glb", 19, -17)
place("environment/lwf_large_barn_v1.glb", 0, -23)
place("environment/lwf_trailer_stage_v2.glb", -16, 11, 90)
place("environment/lwf_farm_gate_posts_v1.glb", 0, 30)
leaf = place("environment/lwf_farm_gate_leaf_v1.glb", -1.65, 30, 72)
# default build layout (BuildLayout.StandardBuildLayout)
x, z = cell(95, 123); place("environment/lwf_free_water_point_v4.glb", x, z)
x, z = cell(172, 140); place("environment/portaloo/lwf_portaloo_v1.glb", x, z)
x, z = cell(144, 119); FOOD = (x, z)
place("environment/lwf_food_van_chassis_v1.glb", x, z); place("environment/lwf_food_van_fascia_v1.glb", x, z)
place("environment/lwf_food_van_serving_flap_v1.glb", x, z)
x, z = cell(160, 120); BAR = (x, z); place("environment/lwf_drinks_stall_prototype_v1.glb", x, z)
x, z = cell(116, 119); place("environment/lwf_first_aid_point_v2.glb", x, z)
x, z = cell(114, 178); place("environment/lwf_security_post_v1.glb", x, z, 90)
STAGE = (-16.0, 11.0)

# a crowd: audience in front of the stage, a bar queue, people on the track
import json
CONTRACT = json.load(open("C:/Projects/festival-tycoon/game/assets/characters/attendee_palette_v1_contract.json"))
def variant(path, ci, hi):
    key = f"{path}#{ci}{hi}"
    if key in _cols:
        return key
    base = asset(path)
    col = bpy.data.collections.new(key.split("/")[-1])
    slots = dict(CONTRACT["clothing_colourways"][ci]["slots"]); slots.update(CONTRACT["hair_colours"][hi]["slots"])
    mapping = {}
    for o in base.objects:
        c = o.copy()
        if o.data is not None:
            c.data = o.data.copy()
            for i, m in enumerate(c.data.materials):
                if m is None:
                    continue
                if m.name not in mapping:
                    m2 = m.copy(); mapping[m.name] = m2
                    for nd in m2.node_tree.nodes:
                        if nd.type == 'TEX_IMAGE' and nd.image is not None:
                            im = nd.image.copy(); px = list(im.pixels); W = im.size[0]
                            for sl, hx in slots.items():
                                rgb = [int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)]
                                for y in range(im.size[1]):
                                    for x in range(8 * int(sl), 8 * int(sl) + 8):
                                        j = (y * W + x) * 4; px[j:j + 3] = rgb
                            im.pixels = px; nd.image = im
                c.data.materials[i] = mapping[m.name]
        col.objects.link(c)
    for c in col.objects:   # keep parenting inside the copy
        if c.parent is not None and c.parent.name in base.objects:
            pass
    _cols[key] = col
    return key

rng = random.Random(4)
bodies = [f"characters/lwf_attendee_{s}_{p}_v2.glb" for s in ("male", "female") for p in ("relaxed", "drink_hold", "relaxed")]
def body():
    return variant(rng.choice(bodies), rng.randrange(4), rng.randrange(4))
if "hairmix" in OPTS:
    exec(open("C:/Users/99wil/AppData/Local/Temp/claude/C--Projects-festival-tycoon/54ef13f4-4691-4176-a16c-f249c73119a8/scratchpad/hair/hairmix.py", encoding="utf-8").read())
def person(x, z, face=None):
    yaw = face if face is not None else rng.uniform(0, 360)
    place(body(), x, z, yaw)
for _ in range(int(16 * CROWD)):   # audience facing the stage (stage faces +X in Godot after its quarter turn)
    place(body(), STAGE[0] + rng.uniform(9.2 if DRESS else 6, 14), STAGE[1] + rng.uniform(-6, 6), -90 + rng.uniform(-25, 25))
for i in range(int(5 * CROWD)):     # bar queue
    person(BAR[0] - 0.2 + rng.uniform(-.2, .2), BAR[1] + 4 + i * 1.1, 0)
for _ in range(int(10 * max(CROWD, 0.5))):
    person(rng.uniform(-3, 3), rng.uniform(-14, 26))
for _ in range(int(6 * CROWD)):
    person(rng.uniform(-10, 24), rng.uniform(-8, 22))
if DRESS:   # picnickers sitting out on the blankets
    for x, z in ((7.6, 13.0), (6.4, 14.0), (10.8, 14.6), (8.6, 17.0), (12.0, 18.0), (5.9, 19.4), (11.5, 21.3)):
        place(body(), x, z, rng.uniform(0, 360))

# ---------------------------------------------------------------- camera (CameraRig.Apply, south view)
cd = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
FOCUS = gd(*[float(v) for v in OPTS['focus'].split(',')]) if 'focus' in OPTS else gd(2.0, 0, 3.0)
yaw = math.radians(45 + 90 * int(OPTS.get("rot", "0")))
cam.location = FOCUS + gd(math.sin(yaw) * 72, 58, math.cos(yaw) * 72)
cam.rotation_euler = (FOCUS - cam.location).to_track_quat('-Z', 'Y').to_euler()
cd.type = 'ORTHO'; cd.ortho_scale = ZOOM * 16 / 9; cd.clip_end = 400
sc.render.resolution_x, sc.render.resolution_y = RES
if OPTS.get("view") == "plan":
    cam.location = (0, 0, 300); cam.rotation_euler = (0, 0, 0); cd.ortho_scale = float(OPTS.get("plan", 300))

# ---------------------------------------------------------------- time of day
T = {
    #           sun elev, travel azimuth (deg, 0 = +X), sun rgb, strength, sun angle, ambient rgb, ambient str, void rgb, exposure
    "game":   (54, 58, (1.00, 0.945, 0.77), 5.0, 1.0, (0.85, 0.90, 0.76), 0.95, (0.28, 0.55, 0.72), 0.1),
    "midday": (64, 58, (1.00, 0.97, 0.86), 5.6, 1.0, (0.82, 0.90, 1.00), 1.00, (0.88, 0.92, 0.95), 0.25),
    "cloud":  (50, 40, (1.00, 0.95, 0.85), 5.0, 1.5, (0.74, 0.84, 1.00), 0.95, (0.64, 0.80, 0.90), 0.1),
    "golden": (15, 62, (1.00, 0.56, 0.22), 11.0, 2.5, (0.55, 0.60, 0.95), 0.42, (0.98, 0.74, 0.50), 0.25),
    "dusk":   (2.5, 2, (1.00, 0.45, 0.35), 1.6, 4.0, (0.36, 0.40, 0.72), 0.42, (0.10, 0.11, 0.22), 0.35),
}[TIME]
elev, az, srgb, sstr, sang, argb, astr, vrgb, expo = T
d = Vector((math.cos(math.radians(az)) * math.cos(math.radians(elev)),
            math.sin(math.radians(az)) * math.cos(math.radians(elev)),
            -math.sin(math.radians(elev))))
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
sun.data.color = srgb; sun.data.energy = sstr; sun.data.angle = math.radians(sang)

w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
nt = w.node_tree; nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputWorld")
amb = nt.nodes.new("ShaderNodeBackground"); amb.inputs[0].default_value = (*argb, 1); amb.inputs[1].default_value = astr
void = nt.nodes.new("ShaderNodeBackground"); void.inputs[0].default_value = (*vrgb, 1); void.inputs[1].default_value = 1.0
lp = nt.nodes.new("ShaderNodeLightPath"); mix = nt.nodes.new("ShaderNodeMixShader")
nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0]); nt.links.new(amb.outputs[0], mix.inputs[1])
nt.links.new(void.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], out.inputs[0])

def emissive(name, rgb, strength):
    m = bpy.data.materials.new(name); m.use_nodes = True
    n = m.node_tree.nodes; n.clear()
    e = n.new("ShaderNodeEmission"); e.inputs[0].default_value = (*rgb, 1); e.inputs[1].default_value = strength
    o = n.new("ShaderNodeOutputMaterial"); m.node_tree.links.new(e.outputs[0], o.inputs[0])
    return m

def matte(name, rgb):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Roughness"].default_value = 0.9
    return m

if TIME == "cloud":
    # drifting cloud shadows: an invisible noise sheet high above the field that only casts shadow
    bpy.ops.mesh.primitive_plane_add(size=260, location=(0, 0, 45))
    cl = bpy.context.object; cl.name = "cloud_shadow_sheet"
    cl.visible_camera = False; cl.visible_diffuse = False; cl.visible_glossy = False
    m = bpy.data.materials.new("cloud"); m.use_nodes = True; n = m.node_tree.nodes; n.clear()
    tc = n.new("ShaderNodeTexCoord"); mp = n.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (0.022, 0.034, 1)
    mp.inputs["Location"].default_value = (0.35, 0.1, 0)
    nz = n.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 2.2; nz.inputs["Detail"].default_value = 3; nz.inputs["Roughness"].default_value = 0.45
    cr = n.new("ShaderNodeValToRGB"); cr.color_ramp.elements[0].position = 0.52; cr.color_ramp.elements[1].position = 0.60
    tr = n.new("ShaderNodeBsdfTransparent"); df = n.new("ShaderNodeBsdfDiffuse"); df.inputs[0].default_value = (0, 0, 0, 1)
    mx = n.new("ShaderNodeMixShader"); o = n.new("ShaderNodeOutputMaterial"); L = m.node_tree.links
    L.new(tc.outputs["Object"], mp.inputs[0]); L.new(mp.outputs[0], nz.inputs[0]); L.new(nz.outputs["Fac"], cr.inputs[0])
    L.new(cr.outputs[0], mx.inputs[0]); L.new(tr.outputs[0], mx.inputs[1]); L.new(df.outputs[0], mx.inputs[2]); L.new(mx.outputs[0], o.inputs[0])
    cl.data.materials.append(m)

if TIME == "dusk":
    # a few warm string lights: stage front to two poles, and between the food van and the bar
    wood = matte("pole", (0.18, 0.12, 0.07)); bulb = emissive("bulb", (1.0, 0.72, 0.38), 40)
    wire = matte("wire", (0.05, 0.05, 0.05))
    def pole(x, z, h=4.2):
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.07, depth=h, location=gd(x, h / 2, z))
        bpy.context.object.data.materials.append(wood)
        return Vector(gd(x, h, z))
    def string(a, b, sag=0.6, spacing=0.9, pools=3):
        n = max(2, int((b - a).length / spacing))
        pts = []
        for i in range(n + 1):
            t = i / n
            p = a.lerp(b, t); p.z -= sag * 4 * t * (1 - t); pts.append(p)
            bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.09, location=p - Vector((0, 0, 0.12)))
            bpy.context.object.data.materials.append(bulb)
        cu = bpy.data.curves.new("wire", 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 0.015
        sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
        for i, p in enumerate(pts):
            sp.points[i].co = (*p, 1)
        ob = bpy.data.objects.new("wire", cu); ob.data.materials.append(wire); sc.collection.objects.link(ob)
        for k in range(pools):
            p = pts[int((k + 0.5) * len(pts) / pools)]
            l = bpy.data.objects.new("pool", bpy.data.lights.new("pool", 'POINT')); sc.collection.objects.link(l)
            l.location = p - Vector((0, 0, 0.3)); l.data.energy = 160; l.data.color = (1.0, 0.7, 0.4); l.data.shadow_soft_size = 0.5
    sx, sz = STAGE
    p1, p2 = pole(sx + 11, sz - 6), pole(sx + 11, sz + 6)
    string(Vector(gd(sx + 1.2, 4.2, sz - 4.6)), p1, pools=3); string(Vector(gd(sx + 1.2, 4.2, sz + 4.6)), p2, pools=3)
    string(p1, p2, sag=0.8, pools=3)
    q1, q2 = pole(FOOD[0] + 1.5, FOOD[1] + 4), pole(BAR[0] - 1.5, BAR[1] + 4)
    string(q1, q2, pools=3)
    # stage wash and a little spill from the vendors
    for off, col, en in ((-2.5, (1.0, 0.62, 0.30), 9000), (2.5, (1.0, 0.45, 0.55), 7000)):
        s = bpy.data.objects.new("stage_spot", bpy.data.lights.new("stage_spot", 'SPOT')); sc.collection.objects.link(s)
        s.location = gd(sx + 6, 6.5, sz + off)
        s.rotation_euler = (Vector(gd(sx, 1.2, sz)) - s.location).to_track_quat('-Z', 'Y').to_euler()
        s.data.energy = en; s.data.color = col; s.data.spot_size = math.radians(48); s.data.spot_blend = 0.6
    for (vx, vz) in (FOOD, BAR):
        l = bpy.data.objects.new("vendor", bpy.data.lights.new("vendor", 'POINT')); sc.collection.objects.link(l)
        l.location = gd(vx, 2.2, vz + 2.4); l.data.energy = 260; l.data.color = (1.0, 0.78, 0.5)
    # farmhouse windows glow
    l = bpy.data.objects.new("house", bpy.data.lights.new("house", 'AREA')); sc.collection.objects.link(l)
    l.location = gd(-22 + 6.5, 3.0, -14 + 5.2); l.data.energy = 300; l.data.color = (1.0, 0.7, 0.4); l.data.size = 4

if "inst" in OPTS:
    exec(open(__file__.replace("farm_scene.py", "instverify.py"), encoding="utf-8").read())
if "py" in OPTS: exec(open(OPTS["py"], encoding="utf-8").read())
if "frame" in OPTS: sc.frame_set(int(OPTS["frame"]))
if "stageglb" in OPTS:   # production stage set parented to the trailer stage's transform
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath="C:/Projects/festival-tycoon/assets/source/environment/stage-sets-v1/out/" + OPTS["stageglb"] + ".glb")
    root = bpy.data.objects.new("stage_set_root", None); sc.collection.objects.link(root)
    root.matrix_world = Matrix.Translation(gd(-16, 0, 11)) @ Matrix.Rotation(math.radians(90), 4, 'Z')
    for o in [o for o in bpy.data.objects if o not in before]:
        if o.parent is None and o is not root: o.parent = root
if "set" in OPTS:
    exec(open(__file__.replace("farm_scene.py", "stageset.py"), encoding="utf-8").read())
if "genre" in OPTS:
    exec(open(__file__.replace("farm_scene.py", "band.py"), encoding="utf-8").read())
if OPTS.get("prodbeauty") == "1":
    exec(open(__file__.replace("farm_scene.py", "prodbeauty.py"), encoding="utf-8").read())
if "glb" in OPTS:
    exec(open(__file__.replace("farm_scene.py", "glbplace.py"), encoding="utf-8").read())
if OPTS.get("beauty") == "1":
    exec(open(__file__.replace("farm_scene.py", "beauty.py"), encoding="utf-8").read())
if "sign" in OPTS:
    exec(open(__file__.replace("farm_scene.py", "gatesign.py"), encoding="utf-8").read())
if LIFE:
    exec(open(__file__.replace("farm_scene.py", "farmlife.py"), encoding="utf-8").read())
if FIELD:
    exec(open(__file__.replace("farm_scene.py", "field.py")).read())
if DRESS:
    exec(open(__file__.replace("farm_scene.py", "dressing.py")).read())
if WORLD:
    HAZE = (0.40, 0.46, 0.47)
    exec(open(__file__.replace("farm_scene.py", "world.py")).read())

# ---------------------------------------------------------------- render
sc.render.engine = 'CYCLES'; sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True
sc.cycles.max_bounces = 4
sc.view_settings.view_transform = 'Standard'
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'Medium Contrast'; sc.view_settings.exposure = expo
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
