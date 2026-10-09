# exec()'d by farm_scene.py (py=...): heavy-rain dressing for the real farm under the game camera.
# OPTS: rmode = dry | onset | heavy | clearing | dusk ; guests = 1|0 ; pud = 1|0 ; extra = path of another hook to exec afterwards
RH = "C:/Projects/festival-tycoon/assets/source/environment/stretch-tent-v1/verification/tools/"
exec(open(RH + "people.py", encoding="utf-8").read())
RMODE = OPTS.get("rmode", "heavy"); WET = {"dry": 0.0, "onset": 0.55, "heavy": 1.0, "clearing": 0.8, "dusk": 1.0}[RMODE]
RAINING = RMODE in ("onset", "heavy", "dusk")
rr = random.Random(11)
sc.cycles.transparent_max_bounces = 128


def srgb(h): return _lin(h)


def flatm(name, hexc, rough=0.8, alpha=1.0, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = srgb(hexc); b.inputs["Roughness"].default_value = rough
    if alpha < 1: b.inputs["Alpha"].default_value = alpha; m.blend_method = 'BLEND'
    if emit: b.inputs["Emission Color"].default_value = srgb(hexc); b.inputs["Emission Strength"].default_value = emit
    return m


# ---------------------------------------------------------------- light: overcast and low for showers
if RMODE in ("onset", "heavy"):
    k = 1.0 if RMODE == "heavy" else 0.0
    sun.data.energy = 2.4 - 1.2 * k; sun.data.angle = math.radians(10 + 18 * k); sun.data.color = (0.92, 0.95, 1.0)
    amb.inputs[0].default_value = (0.66 - 0.06 * k, 0.72 - 0.06 * k, 0.80 - 0.04 * k, 1); amb.inputs[1].default_value = 1.0
    void.inputs[0].default_value = (0.42, 0.46, 0.50, 1); expo = 0.18 - 0.05 * k
if RMODE == "dusk":
    amb.inputs[0].default_value = (0.30, 0.34, 0.50, 1)
if RMODE == "clearing":
    sun.data.energy = 4.6; sun.data.color = (1.0, 0.9, 0.72)

# ---------------------------------------------------------------- wet sheen: darker, glossier surfaces (not the people)
def is_person(m):
    return any(nd.type == 'TEX_IMAGE' and nd.image and "attendee" in nd.image.name.lower() for nd in m.node_tree.nodes) if m.use_nodes else False
WET_SKIP = set()
def wet_all(level):
    if level <= 0: return
    for m in list(bpy.data.materials):
        if not m.use_nodes or m.name in WET_SKIP or is_person(m) or m.name.startswith(("bulb", "pool", "poncho", "rain_", "puddle", "mud_", "splash")): continue
        b = m.node_tree.nodes.get("Principled BSDF")
        if b is None: continue
        r = b.inputs["Roughness"].default_value; b.inputs["Roughness"].default_value = r + (0.22 - r) * level
        bc = b.inputs["Base Color"]
        if bc.is_linked:
            src = bc.links[0].from_socket; mx = m.node_tree.nodes.new("ShaderNodeMix"); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'
            mx.inputs["Factor"].default_value = level; mx.inputs[7].default_value = (0.70, 0.73, 0.76, 1)
            m.node_tree.links.new(src, mx.inputs[6]); m.node_tree.links.new(mx.outputs[2], bc)
        else:
            c = bc.default_value; f = 1 - 0.3 * level; bc.default_value = (c[0] * f, c[1] * f, c[2] * f * 1.02, 1)


# ---------------------------------------------------------------- puddles, mud and splashes
WATER = flatm("puddle_water", "56666e", 0.04); MUD = flatm("mud_churned", "5c4733", 0.35); SLUDGE = flatm("mud_sludge", "6e5a40", 0.18)
SPLASH = flatm("splash_ring", "e8f0f4", 0.2, alpha=0.45)
BLOBN = [0]
def blob(cx, cz, rx, rz, mat, y, n=56, seed=0, rot=0.0):
    BLOBN[0] += 1; y += (BLOBN[0] % 9) * 0.0006                                # no two overlapping blobs coplanar
    g = random.Random(seed); pts = []; ph = [g.uniform(0, 6) for _ in range(3)]
    for i in range(n):
        a = 2 * math.pi * i / n; w = 1 + 0.18 * math.sin(3 * a + ph[0]) + 0.09 * math.sin(5 * a + ph[1]) + 0.04 * math.sin(11 * a + ph[2])
        x, z = rx * w * math.cos(a), rz * w * math.sin(a)
        pts.append(gd(cx + x * math.cos(rot) - z * math.sin(rot), y, cz + x * math.sin(rot) + z * math.cos(rot)))
    c = sum(pts, Vector()) / n; pts = [c] + pts                               # a fan from the centre: no concave n-gons
    faces = [(0, 1 + (i + 1) % n, 1 + i) for i in range(n)]
    me = bpy.data.meshes.new("blob"); me.from_pydata(pts, [], faces); me.update()
    for p_ in me.polygons:
        if p_.normal.z < 0: p_.flip()
    me.update()
    o = bpy.data.objects.new("blob", me); o.data.materials.append(mat); sc.collection.objects.link(o); return o
PUDDLES = []
def mudpatch(cx, cz, r, seed, pools=2, elong=1.0, rot=0.0):
    blob(cx, cz, r, r * elong, MUD, 0.03, seed=seed, rot=rot)
    g = random.Random(seed + 50)
    for k in range(pools):
        px, pz = cx + g.uniform(-0.45, 0.45) * r, cz + g.uniform(-0.45, 0.45) * r * elong
        pr = r * g.uniform(0.22, 0.38); blob(px, pz, pr * 1.3, pr, SLUDGE, 0.036, seed=seed + k * 7, rot=g.uniform(0, 3))
        blob(px, pz, pr * 0.95, pr * 0.7, WATER, 0.042, seed=seed + k * 7 + 1, rot=g.uniform(0, 3)); PUDDLES.append((px, pz, pr * 0.8))
def puddle(cx, cz, r, seed, elong=0.7, rot=0.0):
    blob(cx, cz, r * 1.15, r * elong * 1.15, MUD, 0.031, seed=seed, rot=rot)
    blob(cx, cz, r, r * elong, WATER, 0.04, seed=seed + 1, rot=rot); PUDDLES.append((cx, cz, r * 0.8))
def splash(x, z, r):
    n = 10; inner, outer = [], []; yy = 0.046 + rr.uniform(0, 0.006)
    for i in range(n):
        a = 2 * math.pi * i / n; inner.append(gd(x + r * 0.75 * math.cos(a), yy, z + r * 0.75 * math.sin(a))); outer.append(gd(x + r * math.cos(a), yy, z + r * math.sin(a)))
    me = bpy.data.meshes.new("splash"); me.from_pydata(inner + outer, [], [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]); me.update()
    o = bpy.data.objects.new("splash", me); o.data.materials.append(SPLASH); sc.collection.objects.link(o)
def crown(x, z, s=1.0):                                                      # a drop bouncing back up: a few tiny beads
    for i in range(5):
        a = 2 * math.pi * i / 5
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.018 * s, location=gd(x + 0.05 * s * math.cos(a), 0.05 + 0.04 * s, z + 0.05 * s * math.sin(a)))
        bpy.context.object.data.materials.append(SPLASH)

if OPTS.get("pud", "1") == "1" and WET > 0:
    big = WET >= 0.8
    mudpatch(-9.0, 11.0, 3.2 if big else 2.2, 3, pools=4 if big else 2, elong=1.3)        # in front of the stage
    mudpatch(BAR[0], BAR[1] + 3.2, 2.2 if big else 1.4, 5, pools=2, elong=1.4)           # bar queue
    mudpatch(FOOD[0], FOOD[1] + 3.0, 1.8 if big else 1.2, 7, pools=2, elong=1.2)         # food van queue
    mudpatch(0.0, 27.0, 2.6 if big else 1.6, 9, pools=3, elong=1.0)                     # the gate
    for i, z in enumerate((-6, -1, 4.5, 9, 14, 19.5)):                                   # track ruts
        for x in (-0.9, 0.9):
            if (i + (x > 0)) % 2 == 0 or big: puddle(x + rr.uniform(-0.1, 0.1), z + rr.uniform(-1, 1), rr.uniform(0.5, 0.9) * (1 if big else 0.7), 20 + i * 3 + (x > 0), elong=0.38, rot=math.pi / 2)
    for x, z, r in ((-16.0, -0.6, 0.9), (5.0, 8.0, 0.7), (-3.5, 2.0, 0.6), (12.0, 12.0, 0.8), (22.0, 9.2, 0.7)):    # low spots
        puddle(x, z, r * (1 if big else 0.6), int(x * 7 + z), elong=0.7, rot=rr.uniform(0, 3))
    NOSPLASH = OPTS.get("nosplash") == "1"
    if RAINING and not NOSPLASH:
        for (px, pz, pr) in PUDDLES:
            for _ in range(int(2 + pr * 4)):
                a, d = rr.uniform(0, 6.28), pr * math.sqrt(rr.random()) * 0.9
                splash(px + d * math.cos(a), pz + d * math.sin(a), rr.uniform(0.04, 0.09))
        for _ in range(int(60 * WET)):                                               # on the grass, the track and the mud
            splash(rr.uniform(-20, 24), rr.uniform(-12, 30), rr.uniform(0.03, 0.05))

# ---------------------------------------------------------------- guests out in it
GUESTS = []
def guest(x, z, yaw, pose, sex=None, wet=None, poncho=None, k=[0]):
    k[0] += 1; i = k[0]
    g = Guest(sex or ("male" if i % 2 else "female"), ci=(i * 3) % 4, hi=(i * 5) % 4, wet=(RAINING or RMODE == "clearing") if wet is None else wet, name=f"guest{i}")
    if poncho: make_poncho(g, poncho.replace("PRINT", RH + "poncho_print.png"))
    g.pose(pose); g.place(gd(x, 0, z), yaw); GUESTS.append(g); return g
CAMYAW = 225                                                             # facing the camera
PONCHOS = ["colour:yellow", "colour:pink", "clear", "colour:lime", "brand:PRINT", "colour:sky"]
if OPTS.get("guests", "1") == "1":
    gp = random.Random(5)
    rain = RMODE in ("onset", "heavy", "dusk")
    for i in range(14):                                                  # stage audience: thinner and hunched in the rain
        if rain and RMODE != "onset" and i % 2: continue
        x, z = -16 + gp.uniform(7, 12), 11 + gp.uniform(-5, 5)
        pose = ("hunched" if i % 3 else "hands_head") if rain else "idle"
        guest(x, z, 90 + gp.uniform(-25, 25), pose, poncho=PONCHOS[i % 6] if (rain and i % 4 == 0) else None)
    for i in range(8 if rain else 3):                                    # under the bar's canopy: a huddle
        guest(BAR[0] - 1.1 + (i % 4) * 0.62 + gp.uniform(-.08, .08), BAR[1] + 0.95 + (i // 4) * 0.55, 180 + gp.uniform(-40, 40), "huddle" if rain else "idle")
    for i in range(5):                                                   # along the track
        z = -8 + i * 6.5 + gp.uniform(-1, 1)
        guest(gp.uniform(-1.4, 1.4), z, 0 if i % 2 else 180, "hurry" if rain else "walk", poncho=PONCHOS[(i + 2) % 6] if rain and i == 2 else None)
    for i in range(6):                                                   # out in the field
        x, z = gp.uniform(-6, 20), gp.uniform(-1, 18)
        guest(x, z, gp.uniform(0, 360), ("hands_head" if i % 2 else "hurry") if rain else ("walk" if i % 2 else "idle"),
              poncho=PONCHOS[i % 6] if rain and i % 3 == 0 else None)

wet_all(WET)
if "extra" in OPTS: exec(open(OPTS["extra"], encoding="utf-8").read())
