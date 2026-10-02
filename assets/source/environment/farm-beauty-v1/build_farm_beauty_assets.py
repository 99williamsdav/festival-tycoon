# Farm beauty pass v1: hedgerows, three trees and the farm pond (approved concept 6).
# python make_palette.py ; blender -b --python build_farm_beauty_assets.py -- <out_dir>
# Conventions (Godot after glTF export): +Y up; Blender -Y becomes Godot +Z.
#   Hedge runs: origin at the run's start on the hedge line, run along local +X, depth ±0.5 m, height ≤ 1.76 m,
#   exactly as lwf_hedge_straight_8m_a_v1 / _4m_a_v1 / lwf_hedge_gate_end_v1 so Main.cs placement is unchanged.
#   Trees: origin at the trunk base. Pond: origin at the pond centre, Godot (24, 0, 24.5).
import bpy, bmesh, math, random, sys, os, json
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
PALETTE_PNG = os.path.join(HERE, "tex", "farm_beauty_palette.png")
NSLOT = 16
def U(slot): return ((8 * slot + 4) / (8.0 * NSLOT), 0.5)
G0, G1, G2, G3, G4, G5, BLOSSOM, ROSE, RED, WOOD, MUD, REED, DUCK, DRAKE, BILL, PAD = range(16)
GREENS = (G0, G1, G2, G3, G4, G5)
def T(v): return Matrix.Translation(Vector(v))
def RZ(a): return Matrix.Rotation(a, 4, 'Z')
def RX(a): return Matrix.Rotation(a, 4, 'X')
def D(x, y, z): return Matrix.Diagonal((x, y, z, 1))

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
def palette_material(name):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(PALETTE_PNG); tx.interpolation = 'Closest'
    b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.95
    nt.links.new(tx.outputs[0], b.inputs["Base Color"]); return m

class Mesh:
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def faces_of(self, res):
        return list({f for v in res["verts"] for f in v.link_faces})
    def paint(self, faces, slot_fn):
        for f in faces:
            s = slot_fn(f)
            for l in f.loops:
                l[self.uv].uv = U(s)
    def blob(self, M, slot_fn, sub=2, jitter=0.0, rng=None):
        r = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0, matrix=M)
        if jitter:
            for v in r["verts"]:
                v.co += Vector((rng.uniform(-jitter, jitter), rng.uniform(-jitter, jitter), rng.uniform(-jitter, jitter) * 0.8))
        fs = self.faces_of(r); self.paint(fs, slot_fn); return fs
    def cone(self, M, r1, r2, depth, slot, seg=7, caps=True):
        r = bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=seg, radius1=r1, radius2=r2, depth=depth, matrix=M)
        self.paint(self.faces_of(r), lambda f: slot)
    def box(self, M, size, slot):
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=M @ D(*size)); self.paint(self.faces_of(r), lambda f: slot)
    def poly(self, pts, slot):
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts]); self.paint([f], lambda f: slot); return f
    def link(self, mats):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o

REPORT = {}
def export(name, objs, extra=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True,
                              export_animations=False, export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    ws = [o.matrix_world @ v.co for o in objs if o.type == 'MESH' for v in o.data.vertices]
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons)
    bounds = [[round(min(w[i] for w in ws), 3) for i in range(3)], [round(max(w[i] for w in ws), 3) for i in range(3)]]
    REPORT[name] = dict(tris=tris, blender_bounds=bounds, nodes=[o.name for o in objs], **(extra or {}))
    print(f"EXPORTED {path} tris={tris} bounds={bounds}")

def height_slot(rng, top, var=0.18):
    """dark base fading to lighter tops, broken up per facet"""
    def f(face):
        z = face.calc_center_median().z
        t = max(0.0, min(0.999, z / top + rng.uniform(-var, var)))
        return GREENS[int(t * len(GREENS))]
    return f

# ====================================================================== hedgerows
HEDGE_H, HALF_D = 1.76, 0.5
def hedgerow(name, length, seed, gate_end=False, thin_patch=True):
    reset(); rng = random.Random(seed); PAL = palette_material("LWF_Hedge_MattePalette")
    m = Mesh(name)
    n = max(2, round(length / 0.9))
    xs = [0.25 + (length - 0.5) * k / (n - 1) for k in range(n)] if not gate_end else [0.2, 0.55, 0.82]
    gap = rng.uniform(0.25, 0.75) * length if (thin_patch and length >= 8 and rng.random() < 0.6) else -99
    accents = []
    for k, x in enumerate(xs):
        thin = abs(x - gap) < 0.9
        hmax = 0.8 if thin else rng.uniform(1.3, HEDGE_H)
        if gate_end:
            hmax *= (1.0, 0.95, 0.78)[k]
        x += rng.uniform(-0.15, 0.15) if 0 < k < n - 1 and not gate_end else 0.0
        for layer, (frac, rad, along) in enumerate(((0.32, 0.46, 0.95), (0.62, 0.44, 0.85), (0.86, 0.36, 0.7))):
            if thin and layer == 2:
                continue
            ry = min(rad * rng.uniform(0.85, 1.05), HALF_D - 0.06)
            top = ry * 0.85
            c = Vector((x, rng.uniform(-0.04, 0.04), hmax * frac))
            if c.z + top > HEDGE_H:
                c.z = HEDGE_H - top
            ax = along * rng.uniform(0.75, 1.0)
            if gate_end and k == len(xs) - 1:
                ax *= 0.6                                   # rounded end facing the gate
            m.blob(T(c) @ D(ax, ry, top), height_slot(rng, HEDGE_H), sub=2, jitter=0.12, rng=rng)
            if layer == 2 and rng.random() < 0.3:
                m.blob(T(c + Vector((0, 0, 0.18))) @ D(0.3, 0.26, 0.22), height_slot(rng, HEDGE_H), sub=1, jitter=0.05, rng=rng)
        if rng.random() < 0.3:                              # hawthorn blossom, dog rose or haws on both faces
            kind = rng.choice((BLOSSOM, BLOSSOM, ROSE, RED))
            for _ in range(rng.randint(5, 9)):
                side = rng.choice((-1, 1))
                p = Vector((x + rng.uniform(-.35, .35), side * rng.uniform(0.36, 0.44), rng.uniform(0.5, min(hmax, 1.6))))
                accents.append((p, kind))
        if thin:
            m.cone(T((x, 0, 0.65)), 0.07, 0.06, 1.3, WOOD, seg=6)
            gap = -99
    for p, kind in accents:
        m.blob(T(p) @ D(0.08, 0.08, 0.08), lambda f, s=kind: s, sub=1)
    # hard envelope = the original kit's footprint: runs may overlap their neighbour by 0.1 m; the gate end stops at 1.0
    x_hi = length if gate_end else length + 0.1
    for v in m.bm.verts:
        v.co.x = min(max(v.co.x, -0.1), x_hi)
        v.co.y = min(max(v.co.y, -HALF_D), HALF_D)
        v.co.z = min(max(v.co.z, 0.0), HEDGE_H)
    o = m.link([PAL])
    export(name, [o])

hedgerow("lwf_hedge_straight_8m_a_v1", 8.0, 101)
hedgerow("lwf_hedge_straight_8m_b_v1", 8.0, 202)
hedgerow("lwf_hedge_straight_8m_c_v1", 8.0, 303)
hedgerow("lwf_hedge_straight_8m_d_v1", 8.0, 404)
hedgerow("lwf_hedge_straight_4m_a_v1", 4.0, 505, thin_patch=False)
hedgerow("lwf_hedge_gate_end_v1", 1.0, 606, gate_end=True, thin_patch=False)

# ====================================================================== trees (origin at trunk base)
def tree(name, h, crown, lobes, trunk_r, seed, top_slot, fruit=False):
    reset(); rng = random.Random(seed); PAL = palette_material("LWF_Tree_MattePalette")
    trunk = Mesh("Trunk"); cr = Mesh("Crown")
    trunk.cone(T((0, 0, h * 0.275)), trunk_r, trunk_r * 0.6, h * 0.55, WOOD, seg=7)
    for k in range(3):                                      # a couple of stout limbs into the crown
        a = k * 2.1 + rng.uniform(-.3, .3)
        trunk.cone(T((math.cos(a) * crown * 0.18, math.sin(a) * crown * 0.18, h * 0.55)) @ Matrix.Rotation(0.5, 4, Vector((-math.sin(a), math.cos(a), 0))),
                   trunk_r * 0.45, trunk_r * 0.25, h * 0.25, WOOD, seg=5)
    lo = h * 0.4
    def leaf(f):
        z = f.calc_center_median().z
        t = max(0.0, min(0.999, (z - lo) / (h - lo) + rng.uniform(-0.2, 0.2)))
        return GREENS[min(top_slot, int(t * (top_slot + 1)))]
    for k in range(lobes):
        a = rng.uniform(0, 6.28); d_ = rng.uniform(0, crown * 0.55)
        c = Vector((math.cos(a) * d_, math.sin(a) * d_, h * rng.uniform(0.55, 0.85)))
        s = crown * rng.uniform(0.45, 0.65)
        cr.blob(T(c) @ D(s, s, s * 0.8), leaf, sub=1, jitter=0.06 * s, rng=rng)
    if fruit:
        for _ in range(14):
            a = rng.uniform(0, 6.28)
            cr.blob(T((math.cos(a) * crown * 0.78, math.sin(a) * crown * 0.78, h * rng.uniform(0.45, 0.75))) @ D(0.09, 0.09, 0.09), lambda f: RED, sub=1)
    objs = [trunk.link([PAL]), cr.link([PAL])]
    export(name, objs, dict(trunk_radius_m=trunk_r, crown_radius_m=crown, height_m=h))

tree("lwf_tree_oak_v1", 10.5, 4.6, 9, 0.45, 1, G4)
tree("lwf_tree_field_maple_v1", 7.5, 3.2, 7, 0.30, 2, G5)
tree("lwf_tree_old_apple_v1", 4.6, 2.0, 5, 0.18, 3, G5, fruit=True)

# ====================================================================== pond (origin at the pond centre)
reset(); rng = random.Random(5)
PAL = palette_material("LWF_Pond_MattePalette")
WATER = bpy.data.materials.new("LWF_Pond_Water"); WATER.use_nodes = True
wb = WATER.node_tree.nodes["Principled BSDF"]; wb.inputs["Base Color"].default_value = (0.049, 0.111, 0.122, 1)   # sRGB 3E5E62
wb.inputs["Roughness"].default_value = 0.12
def ring(rx, rz, k, jit, z):
    pts = []
    for i in range(k):
        t = 2 * math.pi * i / k; f = 1 + rng.uniform(-jit, jit)
        gx, gz = rx * math.cos(t) * f, rz * math.sin(t) * f          # Godot local x, z
        pts.append(Vector((gx, -gz, z)))
    return pts
MUD_RING = ring(5.0, 3.9, 18, 0.12, 0.06)
WATER_RING = ring(4.0, 3.0, 18, 0.12, 0.075)
margin = Mesh("PondMargin"); margin.poly(MUD_RING, MUD)
for i in range(70):                                                 # reed clumps on the north and west (hedge) side
    t = rng.uniform(math.pi * 0.7, math.pi * 1.55); f = rng.uniform(0.85, 1.05)
    c = Vector((4.3 * math.cos(t) * f, -3.3 * math.sin(t) * f, 0)); hh = rng.uniform(0.8, 1.5)
    margin.cone(T(c + Vector((0, 0, hh / 2))) @ RX(rng.uniform(-.2, .2)), 0.04, 0.0, hh, REED if i % 3 else G3, seg=3, caps=False)
for i in range(7):                                                  # lily pads on the water
    c = Vector((rng.uniform(0.5, 2.5), -rng.uniform(-1.5, 1.2), 0.085))
    r = bmesh.ops.create_circle(margin.bm, cap_ends=True, segments=7, radius=rng.uniform(0.2, 0.32), matrix=T(c))
    margin.paint(margin.faces_of(r), lambda f: PAD)
water = Mesh("PondWater"); water.poly(WATER_RING, 0)
ducks = Mesh("Ducks")
for k, (dx, dz, yaw) in enumerate(((-1.0, 0.6, 0.6), (-0.2, 1.3, 1.1))):
    base = T((dx, -dz, 0.1)) @ RZ(yaw)
    ducks.blob(base @ T((0, 0, 0.1)) @ D(0.26, 0.15, 0.12), lambda f: DUCK, sub=1)
    ducks.blob(base @ T((0.22, 0, 0.28)) @ D(0.09, 0.09, 0.09), lambda f, k=k: DRAKE if k == 0 else DUCK, sub=1)
    ducks.box(base @ T((0.33, 0, 0.27)), (0.1, 0.05, 0.03), BILL)
objs = [margin.link([PAL]), water.link([WATER]), ducks.link([PAL])]
# footprint: the mud ring polygon (the water sits inside it), in Godot world x/z
CX, CZ = 24.0, 24.5
poly = [[round(CX + p.x, 3), round(CZ - p.y, 3)] for p in MUD_RING]
def inside(x, z):
    c = False
    for i in range(len(poly)):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % len(poly)]
        if (az > z) != (bz > z) and x < (bx - ax) * (z - az) / (bz - az) + ax:
            c = not c
    return c
cells = []
for ix in range(int((CX - 6 + 64) / 0.5), int((CX + 6 + 64) / 0.5) + 1):
    for iz in range(int((CZ - 5 + 64) / 0.5), int((CZ + 5 + 64) / 0.5) + 1):
        # a cell is blocked if any of its centre or corners falls inside the margin polygon
        x0, z0 = ix * 0.5 - 64, iz * 0.5 - 64
        if any(inside(x, z) for x, z in ((x0 + .25, z0 + .25), (x0, z0), (x0 + .5, z0), (x0, z0 + .5), (x0 + .5, z0 + .5))):
            cells.append([ix, iz])
export("lwf_farm_pond_v1", objs, dict(place_at_godot=[CX, 0, CZ], footprint_polygon_godot_xz=poly, blocked_cells=cells,
                                      blocked_cell_count=len(cells)))
json.dump(REPORT, open(os.path.join(OUT, "farm_beauty_report.json"), "w"), indent=1)
print("CELLS", len(cells))
