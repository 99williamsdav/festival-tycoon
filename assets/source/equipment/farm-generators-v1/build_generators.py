# Farm generators v1: the approved uncaged Tier 1 farm diesel and the rental hire set, four states each.
#   blender -b --python build_generators.py -- <out_dir>
# Runtime pattern as lwf_towable_generator_*_v1.glb: node <Prefix>_Base + node <Prefix>_State_<State>, one matte palette
# material (plus a small emissive companion for glow and sparks), origin at ground centre, Blender +X = Godot +X.
# Placed at the generator position (-20.25, 14.5) the trailer stage's back rail is about 2.1 m away along +X: the
# orange lead runs that way, and the sockets / control door face +X (towards the stage and the default camera).
import bpy, bmesh, math, random, os, sys, json, hashlib
from mathutils import Vector as V, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
STATES = ("normal", "overloaded", "fault", "isolated")
REPORT = {}

# palette: name -> (hex, emissive?)
COL = dict(green="5f7a4a", green_dk="465c37", rust="8a4f2c", rust2="a2643a", steel="4a4d50", steel_dk="2e3134", engine="3b3f42",
           wood="b08a5a", wood_dk="8d6b44", tank="b5532f", tank_dk="8e3f24", cable="d9862b", jerry="a8432f", chrome="9a9da2",
           plug_y="e0b13a", plug_b="3c6f9e", cream="e9e2cf", cream_dk="cfc6ae", stripe="2b6e66", stripe2="d8a43b", tyre="1d1f20",
           smoke="9a9a95", smoke_dk="3a3836", smoke_bk="1f1e1d", soot="1e1e1e", tag="c0392b", shake="f5ebd6")
GLOW = dict(hot="ff7a1a", glow="ff8a2a", spark="ffd35a", flash="fff0b0")


class Kit:
    """bmesh parts per (group, colour); group 'base' or 'state'"""
    def __init__(self):
        self.parts = {}; self.group = "base"
    def bm(self, col):
        k = (self.group, col)
        if k not in self.parts: self.parts[k] = bmesh.new()
        return self.parts[k]
    def box(self, c, s, col, rot=None):
        M = Matrix.Translation(V(c)) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*s, 1))
        bmesh.ops.create_cube(self.bm(col), size=1.0, matrix=M)
    def cyl(self, c, r, depth, col, axis='Z', seg=8, rot=None):
        R = {'X': Matrix.Rotation(math.pi / 2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 4, 'X'), 'Z': Matrix()}[axis]
        if rot: R = rot.to_matrix().to_4x4() @ R
        bmesh.ops.create_cone(self.bm(col), cap_ends=True, segments=seg, radius1=r, radius2=r, depth=depth, matrix=Matrix.Translation(V(c)) @ R)
    def ball(self, c, r, col, squash=(1, 1, 0.85)):
        bmesh.ops.create_icosphere(self.bm(col), subdivisions=1, radius=r, matrix=Matrix.Translation(V(c)) @ Matrix.Diagonal((*squash, 1)))
    def tube(self, pts, r, col, seg=5):
        for a, b in zip(pts, pts[1:]):
            a, b = V(a), V(b); d = b - a
            self.cyl((a + b) / 2, r, d.length + r * 0.6, col, seg=seg, rot=d.to_track_quat('Z', 'Y').to_euler())


def coil(k, c, r, col, loops=3, wire=0.018):
    for i in range(loops):
        rr = r * (1 - 0.06 * i)
        k.tube([V(c) + V((rr * math.cos(t), rr * math.sin(t) * 0.92, 0.02 + i * 0.03)) for t in [2 * math.pi * j / 10 for j in range(11)]], wire, col)


# ---------------------------------------------------------------- farm diesel (uncaged), about 1.45 x 0.95 x 1.15 m
def farm_diesel(state, seed=3):
    rng = random.Random(seed); k = Kit(); z0 = 0.13
    for y in (-0.42, 0.0, 0.42): k.box((0, y, 0.05), (1.45, 0.10, 0.10), "wood_dk")
    for x in (-0.62, -0.31, 0.0, 0.31, 0.62): k.box((x, 0, 0.115), (0.13, 1.0, 0.025), "wood")
    for y in (-0.36, 0.36): k.box((0, y, z0 + 0.04), (1.32, 0.07, 0.08), "green")
    for x in (-0.45, 0.1, 0.5): k.box((x, -0.396, z0 + 0.04), (rng.uniform(.08, .16), 0.006, 0.05), rng.choice(("rust", "rust2")))
    for x in (-0.30, 0.30):
        for y in (-0.17, 0.17): k.box((x, y, z0 + 0.38), (0.04, 0.04, 0.56), "steel_dk")
        k.box((x, 0, z0 + 0.635), (0.05, 0.40, 0.04), "steel_dk")
    k.box((0.64, -0.12, z0 + 0.30), (0.04, 0.04, 0.50), "steel_dk")
    k.box((-0.28, 0, z0 + 0.30), (0.46, 0.46, 0.36), "engine")
    for i in range(4): k.box((-0.28, 0, z0 + 0.52 + i * 0.035), (0.40 - i * 0.02, 0.40, 0.016), "steel_dk")
    k.cyl((-0.52, 0, z0 + 0.32), 0.19, 0.06, "steel_dk", axis='X', seg=10)
    k.cyl((-0.56, 0, z0 + 0.32), 0.05, 0.05, "steel", axis='X', seg=6)
    k.cyl((0.22, 0, z0 + 0.30), 0.19, 0.42, "green", axis='X', seg=10)
    k.cyl((0.44, 0, z0 + 0.30), 0.17, 0.04, "green_dk", axis='X', seg=10)
    for i in range(5): k.box((0.44, -0.08 + i * 0.04, z0 + 0.30), (0.045, 0.012, 0.18), "steel_dk")
    k.box((0.22, 0.18, z0 + 0.18), (0.10, 0.08, 0.06), "rust")
    k.box((0.0, 0, z0 + 0.74), (0.62, 0.42, 0.20), "tank"); k.box((0.0, 0, z0 + 0.85), (0.56, 0.36, 0.03), "tank_dk")
    k.cyl((0.18, 0.08, z0 + 0.88), 0.045, 0.05, "steel_dk", seg=8); k.box((-0.15, -0.21, z0 + 0.70), (0.2, 0.012, 0.08), "rust2")
    k.box((0.665, -0.12, z0 + 0.66), (0.05, 0.28, 0.24), "steel_dk")
    k.cyl((0.695, -0.19, z0 + 0.70), 0.035, 0.02, "plug_y", axis='X', seg=8)
    k.cyl((0.695, -0.06, z0 + 0.70), 0.035, 0.02, "plug_b", axis='X', seg=8)
    k.cyl((0.695, -0.12, z0 + 0.59), 0.03, 0.02, "cream", axis='X', seg=8)
    coil(k, (1.18, -0.42, 0.0), 0.16, "cable")
    k.tube([(1.30, -0.36, 0.03), (1.65, -0.22, 0.02), (2.05, -0.08, 0.02)], 0.016, "cable")       # on towards the stage
    k.box((-0.35, -0.72, 0.18), (0.30, 0.16, 0.36), "jerry", rot=Euler((0, 0, 0.2)))
    k.box((-0.35, -0.72, 0.38), (0.12, 0.10, 0.05), "jerry", rot=Euler((0, 0, 0.2)))
    k.cyl((-0.25, -0.70, 0.39), 0.025, 0.04, "steel_dk", seg=6)
    # ---- state part: exhaust (its colour changes), the socket end of the lead, and the effects
    k.group = "state"
    exh = {"overloaded": "hot", "fault": "soot"}.get(state, "steel_dk")
    k.cyl((-0.42, -0.25, z0 + 0.62), 0.07, 0.24, exh, axis='X', seg=8)
    k.tube([(-0.32, -0.25, z0 + 0.62), (-0.32, -0.25, z0 + 1.18)], 0.025, exh, seg=6)
    k.box((-0.32, -0.25, z0 + 1.205), (0.07, 0.07, 0.01), "steel_dk", rot=Euler((0.4, 0, 0)))
    if state != "isolated":
        k.box((0.72, -0.19, z0 + 0.70), (0.06, 0.05, 0.05), "plug_y")
        k.tube([(0.75, -0.19, z0 + 0.70), (0.86, -0.22, z0 + 0.45), (0.92, -0.26, 0.03), (1.05, -0.35, 0.02)], 0.016, "cable")
    else:
        k.box((0.86, -0.30, 0.04), (0.06, 0.07, 0.05), "plug_y", rot=Euler((0, 0, 0.6)))
        k.tube([(0.88, -0.32, 0.03), (1.05, -0.35, 0.02)], 0.016, "cable")
        k.box((0.695, -0.12, z0 + 0.50), (0.012, 0.06, 0.09), "tag")
    sx, sy, sz = -0.32, -0.25, z0 + 1.22
    puffs = {"normal": (("smoke", [(0.0, .05, 0.06), (0.05, .17, 0.08)])),
             "overloaded": ("smoke_dk", [(0.0, .06, 0.08), (0.06, .2, 0.11), (0.14, .38, 0.14), (0.24, .58, 0.16)]),
             "fault": ("smoke_bk", [(0.0, .06, 0.09), (0.05, .22, 0.13), (0.12, .44, 0.17), (0.2, .68, 0.2), (0.3, .92, 0.22)])}.get(state)
    if puffs:
        for dx, dz, r in puffs[1]: k.ball((sx + dx, sy - dx * 0.4, sz + dz), r, puffs[0])
    if state == "overloaded":
        k.box((-0.28, 0.235, z0 + 0.30), (0.30, 0.01, 0.16), "glow")
        for side in (-1, 1):
            for i in range(3): k.box((side * (0.82 + i * 0.07), 0, z0 + 0.35 + i * 0.18), (0.03, 0.03, 0.16), "shake", rot=Euler((0, side * 0.35, 0)))
    if state == "fault":
        for i in range(10):
            a = rng.uniform(0, 2 * math.pi); r = rng.uniform(0.08, 0.32)
            k.box((0.72 + abs(math.cos(a)) * r * 0.6, -0.12 + math.sin(a) * r * 0.6, z0 + 0.70 + math.sin(a) * r),
                  (0.02, 0.02, rng.uniform(0.07, 0.14)), "spark", rot=Euler((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)))
        k.ball((0.72, -0.15, z0 + 0.70), 0.05, "flash", squash=(1, 1, 1))
        k.ball((0.62, -0.05, z0 + 0.90), 0.10, "smoke_bk", squash=(1, 1, 1))
    return k, dict(smoke_origin=[sx, sy, sz], sockets=[0.695, -0.12, z0 + 0.66])


# ---------------------------------------------------------------- hire set, rotated so the control door and reel face +X
def hire_set(state, seed=5):
    rng = random.Random(seed); k = Kit()
    def r180(p): return (-p[0], -p[1], p[2])
    B = lambda c, s, col, rot=None: k.box(r180(c), s, col, rot)
    Cy = lambda c, r, d, col, axis='Z', seg=8: k.cyl(r180(c), r, d, col, axis=axis, seg=seg)
    T = lambda pts, r, col: k.tube([r180(p) for p in pts], r, col)
    B((0, 0, 0.42), (1.9, 1.0, 0.08), "steel_dk")
    for y in (-0.62, 0.62):
        Cy((0, y, 0.30), 0.30, 0.18, "tyre", axis='Y', seg=12); Cy((0, y + 0.01 * (1 if y > 0 else -1), 0.30), 0.14, 0.19, "chrome", axis='Y', seg=8)
        B((0, y, 0.62), (0.72, 0.22, 0.04), "steel_dk"); B((0.36, y, 0.50), (0.04, 0.22, 0.24), "steel_dk"); B((-0.36, y, 0.50), (0.04, 0.22, 0.24), "steel_dk")
    T([(0.9, 0.3, 0.42), (1.6, 0, 0.42)], 0.04, "steel_dk"); T([(0.9, -0.3, 0.42), (1.6, 0, 0.42)], 0.04, "steel_dk")
    Cy((1.66, 0, 0.42), 0.07, 0.10, "steel")
    T([(1.25, 0.12, 0.42), (1.25, 0.12, 0.10)], 0.03, "steel"); Cy((1.25, 0.12, 0.07), 0.07, 0.05, "tyre", axis='Y')
    for y in (-0.4, 0.4): T([(-0.85, y, 0.42), (-0.85, y, 0.02)], 0.03, "steel")
    B((-0.05, 0, 1.0), (1.75, 1.0, 1.08), "cream"); B((-0.05, 0, 1.56), (1.80, 1.05, 0.05), "cream_dk")
    for side in (-1, 1):
        y = side * 0.505
        B((-0.05, y, 0.78), (1.76, 0.012, 0.12), "stripe"); B((-0.05, y, 0.86), (1.76, 0.012, 0.03), "stripe2")
        for i in range(6): B((0.55, y, 1.05 + i * 0.07), (0.42, 0.014, 0.025), "cream_dk")
        B((-0.35, y, 1.31), (0.62, 0.012, 0.012), "cream_dk"); B((-0.66, y, 1.0), (0.012, 0.012, 0.62), "cream_dk"); B((-0.04, y, 1.0), (0.012, 0.012, 0.62), "cream_dk")
        B((-0.10, y, 1.0), (0.05, 0.02, 0.10), "steel_dk")
    B((-0.94, 0, 1.0), (0.012, 0.9, 0.9), "cream_dk"); B((-0.945, 0.25, 0.98), (0.03, 0.30, 0.22), "steel_dk")
    B((0.0, 0, 1.62), (0.18, 0.10, 0.06), "steel")
    Cy((-1.2, 0.6, 0.12), 0.16, 0.10, "steel_dk", axis='Y', seg=10); Cy((-1.2, 0.6, 0.12), 0.12, 0.11, "cable", axis='Y', seg=10)
    T([(-1.2, 0.48, 0.03), (-1.6, 0.3, 0.02), (-2.05, 0.10, 0.02)], 0.016, "cable")              # reel to the stage
    k.group = "state"
    exh = {"overloaded": "hot", "fault": "soot"}.get(state, "steel_dk")
    Cy((0.45, -0.25, 1.62), 0.05, 0.14, exh)
    if state != "isolated":
        T([(-0.96, 0.25, 0.92), (-1.05, 0.45, 0.4), (-1.2, 0.6, 0.15)], 0.016, "cable")
    else:
        T([(-1.12, 0.52, 0.05), (-1.2, 0.6, 0.15)], 0.016, "cable"); B((-1.06, 0.46, 0.04), (0.07, 0.06, 0.05), "plug_y", rot=Euler((0, 0, 0.5)))
        B((-0.965, 0.25, 0.86), (0.012, 0.06, 0.09), "tag")
    sx, sy, sz = 0.45, -0.25, 1.72
    puffs = {"normal": ("smoke", [(0.0, .05, 0.07), (0.06, .18, 0.09)]),
             "overloaded": ("smoke_dk", [(0.0, .06, 0.09), (0.07, .22, 0.12), (0.16, .42, 0.15), (0.26, .64, 0.18)]),
             "fault": ("smoke_bk", [(0.0, .07, 0.10), (0.06, .25, 0.14), (0.14, .48, 0.18), (0.24, .74, 0.22), (0.36, 1.0, 0.24)])}.get(state)
    if puffs:
        for dx, dz, r in puffs[1]: k.ball(r180((sx + dx, sy - dx * 0.4, sz + dz)), r, puffs[0])
    if state == "overloaded":
        for i in range(6): B((0.55, 0.512, 1.05 + i * 0.07), (0.40, 0.006, 0.02), "glow")             # louvres glowing
        for side in (-1, 1):
            for i in range(3): k.box((side * (1.05 + i * 0.07), 0, 0.7 + i * 0.22), (0.03, 0.03, 0.18), "shake", rot=Euler((0, side * 0.35, 0)))
    if state == "fault":
        for i in range(10):
            a = rng.uniform(0, 2 * math.pi); r = rng.uniform(0.08, 0.3)
            B((-0.97 - abs(math.cos(a)) * r * 0.6, 0.25 + math.sin(a) * r * 0.6, 0.98 + math.sin(a) * r), (0.02, 0.02, rng.uniform(0.07, 0.14)), "spark",
              rot=Euler((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)))
        k.ball(r180((-0.98, 0.22, 0.98)), 0.05, "flash", squash=(1, 1, 1))
    return k, dict(smoke_origin=list(r180((sx, sy, sz))), control_door=list(r180((-0.945, 0.25, 0.98))))


# ---------------------------------------------------------------- palette, export
def palette(name):
    keys = list(COL) + list(GLOW)
    W = 8 * len(keys); img = bpy.data.images.new(name, W, 8)
    def lin(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    px = []
    for y in range(8):
        for kname in keys:
            hx = COL.get(kname) or GLOW[kname]; px += ([int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    return img, {kname: (8 * i + 4) / W for i, kname in enumerate(keys)}, keys


def material(name, img, emissive=False):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.92; b.inputs["Metallic"].default_value = 0.0
    if emissive:
        m.node_tree.links.new(tx.outputs[0], b.inputs["Emission Color"]); b.inputs["Emission Strength"].default_value = 3.0
    return m


def build(prefix, palname, fn, state, kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    img, U, keys = palette(palname)
    mat = material(f"{prefix}_MattePalette", img); glow = material(f"{prefix}_Glow", img, emissive=True)
    k, info = fn(state)
    objs = []
    for group, node in (("base", f"{prefix}_Base"), ("state", f"{prefix}_State_{state.capitalize()}")):
        bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
        glow_faces = set()
        for (g, col), part in k.parts.items():
            if g != group: continue
            me = bpy.data.meshes.new("tmp"); part.to_mesh(me); part.free()
            start = len(bm.faces); bm.from_mesh(me); bpy.data.meshes.remove(me)
            bm.faces.ensure_lookup_table(); uvl = bm.loops.layers.uv.active
            for f in bm.faces[start:]:
                for l in f.loops: l[uvl].uv = (U[col], 0.5)
                if col in GLOW: glow_faces.add(f.index)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        me = bpy.data.meshes.new(node); bm.to_mesh(me); bm.free()
        me.materials.append(mat)
        if glow_faces:
            me.materials.append(glow)
            for p in me.polygons:
                if p.index in glow_faces: p.material_index = 1
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(node, me); bpy.context.scene.collection.objects.link(o); objs.append(o)
    name = f"lwf_{kind}_{state}_v1"
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    ws = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    REPORT[name] = dict(file=name + ".glb", nodes=[o.name for o in objs], triangles=sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons),
                        bounds_blender_m=[[round(min(w[i] for w in ws), 3) for i in range(3)], [round(max(w[i] for w in ws), 3) for i in range(3)]],
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), palette=f"{palname} ({8 * len(keys)}x8, slots: {', '.join(keys)})", **info)
    print("EXPORTED", name, REPORT[name]["triangles"])


for st in STATES:
    build("LWF_FarmDiesel", "lwf_farm_diesel_palette", farm_diesel, st, "farm_diesel")
    build("LWF_HireGenerator", "lwf_hire_generator_palette", hire_set, st, "hire_generator")
json.dump(REPORT, open(os.path.join(OUT, "generators_report.json"), "w"), indent=1)
