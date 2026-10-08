# Cow pasture v1 production (approved: pasture as boarded, gate A weathered oak, chewed cable C).
# Board: Documents/Festival Tycoon concepts/cow-pasture/.
#   blender -b --python build_cow_pasture.py -- <out_dir>
# Godot axes in the code and the JSON (Blender (x, y, z) = Godot (x, -z, y)); one shared matte palette (Closest) for all,
# plus one emissive material for the spark.
import bpy, bmesh, math, sys, os, json, hashlib, random
from mathutils import Vector as V, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
REPORT = {}


def G(x, y, z): return V((x, -z, y))
def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)


PAL = ["a08a69", "856f50", "b09a78", "2e3134", "e8712a", "c9a46c", "a8854f", "6e8c47", "7f9a56", "94996a", "7d6e4c", "5a4630",
       "7b5aa6", "4f7a3a", "a9aeb0", "7f868a", "4a7a94", "d6b45a", "8a6e4c", "1d1f21", "c8743a", "2a2622", "808080", "808080"]
(OAK, OAK_DK, OAK_LT, IRON, TWINE, PALLET, PALLET_DK, PASTURE, GRAZED, WORN, MUD, PAT, THISTLE, LEAF, GALV, GALV_DK, WATER, HAY,
 FENCE, CABLE, COPPER, SCORCH) = range(22)
PW = 8 * len(PAL)
def U(slot): return ((8 * slot + 4) / PW, 0.5)


def palette_mat():
    m = bpy.data.materials.new("LWF_CowPasture_MattePalette"); m.use_nodes = True
    img = bpy.data.images.new("lwf_cow_pasture_palette", PW, 8); px = []
    for y in range(8):
        for hx in PAL: px += ([int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.85; b.inputs["Specular IOR Level"].default_value = 0.3
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); return m


def spark_mat():
    m = bpy.data.materials.new("LWF_Spark_Emissive"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (1.0, 0.89, 0.36, 1); b.inputs["Emission Color"].default_value = (1.0, 0.86, 0.32, 1)
    b.inputs["Emission Strength"].default_value = 6.0; return m


def export(objs, name, **meta):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
        for c in o.children_recursive: c.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    meshes = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    REPORT[name] = dict(file=name + ".glb", nodes=sorted(o.name for o in bpy.context.selected_objects),
                        triangles=sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons),
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), **meta)
    print("EXPORTED", name, REPORT[name]["triangles"])


class Kit:
    """one bmesh per mesh; every face takes a fixed UV (a palette swatch centre). Coordinates are GODOT (x, y, z)."""
    def __init__(self, name): self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def _paint(self, verts, uv):
        for f in {f for v in verts for f in v.link_faces}:
            for l in f.loops: l[self.uv].uv = uv
    def box(self, c, s, slot, yaw=0.0, tilt=None):
        """c centre, s (sx, sy, sz) Godot sizes; yaw degrees about +Y; tilt (axis, deg) applied first in the box frame"""
        R = Matrix.Rotation(math.radians(yaw), 4, 'Z')
        if tilt: R = R @ Matrix.Rotation(math.radians(tilt[1]), 4, {'x': 'X', 'z': 'Y'}[tilt[0]])
        M = Matrix.Translation(G(*c)) @ R @ Matrix.Diagonal((s[0], s[2], s[1], 1))
        self._paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=M)["verts"], U(slot))
    def bar(self, a, b, r, slot, seg=4):
        """square-ish iron bar between Godot points a and b (seg 4 = square section)"""
        a, b = G(*a), G(*b); d = b - a
        M = Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ Matrix.Rotation(math.pi / 4, 4, 'Z')
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length + r, matrix=M)["verts"], U(slot))
    def cone(self, c, r, h, slot, seg=4):
        M = Matrix.Translation(G(*c) + V((0, 0, h / 2))) @ Matrix.Rotation(math.pi / 4, 4, 'Z')
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=0.0, depth=h, matrix=M)["verts"], U(slot))
    def cyl(self, c, r, h, slot, seg=8, r2=None):
        M = Matrix.Translation(G(*c) + V((0, 0, h / 2)))
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h, matrix=M)["verts"], U(slot))
    def ball(self, c, r, slot, sub=1):
        M = Matrix.Translation(G(*c)) @ Matrix.Diagonal((r, r, r, 1))
        self._paint(bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0, matrix=M)["verts"], U(slot))
    def flat(self, poly, y, slot):
        """flat polygon at height y from Godot (x, z) points (counter-clockwise seen from above is fine either way)"""
        vs = [self.bm.verts.new(G(x, y, z)) for x, z in poly]; f = self.bm.faces.new(vs)
        if f.normal.z < 0: f.normal_flip()
        for l in f.loops: l[self.uv].uv = U(slot)
    def link(self, mat, at=None, rot_z=0.0, recalc=True):
        """at: Godot origin of the object (geometry was authored relative to it); recalc=False keeps flat strips facing up"""
        if recalc: bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o)
        if at is not None: o.location = G(*at)
        o.rotation_euler = (0, 0, math.radians(rot_z)); return o




def empty(name, at=(0, 0, 0), rot_z=0.0, parent=None):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    if parent: e.parent = parent
    e.location = G(*at); e.rotation_euler = (0, 0, math.radians(rot_z)); return e


def blender_xf(o, M):
    o.data.transform(M); o.data.update()


# ================================================================ 1. lwf_field_gate_oak_v1
# Origin: the centre of the hedge opening on the ground (world (32, 0, -2.65), yaw 0). The gate line runs along local Z; the
# pasture is +X, the festival site is -X. Hanging post at local z +1.5, slam post at z -1.5 (clear opening about 2.8 m).
reset(); pm = palette_mat()
HZ, SZ = 1.5, -1.5
HINGE = (0.0, 0.0, 1.37)
LL = 2.78                                                     # leaf length, hinge to the far stile
k = Kit("LWF_FieldGate_Posts")
k.box((0, 0.75, HZ), (0.22, 1.5, 0.22), OAK_DK); k.box((0, 1.52, HZ), (0.24, 0.04, 0.24), OAK)
k.box((0, 0.675, SZ), (0.17, 1.35, 0.17), OAK_DK); k.box((0, 1.37, SZ), (0.19, 0.04, 0.19), OAK)
for y in (0.16, 1.08): k.bar((0.0, y, HZ - 0.11), (0.0, y, HZ - 0.15), 0.014, IRON)          # hinge pins on the hanging post
k.box((0.0, 0.92, SZ + 0.09), (0.05, 0.08, 0.03), IRON)                                         # catch keeper on the slam post
posts = k.link(pm)


def leaf_kit(name, broken):
    """the leaf along its own +X from the hinge axis (x 0) to the far stile, bottom at y 0"""
    k = Kit(name); L = LL
    bars = [0.14, 0.36, 0.58, 0.80, 1.06]
    k.box((0.05, 0.60, 0), (0.10, 1.14, 0.10), OAK)                                           # hanging stile
    k.box((L - 0.045, 0.58, 0), (0.09, 1.10, 0.08), OAK)                                      # shutting stile
    k.box((L * 0.5, 0.58, 0), (0.07, 1.0, 0.06), OAK_DK)                                      # mid upright
    for i, y in enumerate(bars):
        if broken and i == 2: continue                                                        # smashed bar
        if broken and i == 4:                                                                 # split top rail
            k.box((L * 0.27, y, 0), (L * 0.52, 0.12, 0.06), OAK)
            k.box((L * 0.76, y - 0.06, 0), (L * 0.46, 0.11, 0.06), OAK, tilt=('z', 8))
            continue
        k.box((L / 2, y, 0), (L, 0.13 if i == 4 else 0.10, 0.05), OAK)
    ang = math.degrees(math.atan2(0.9, L * 0.55))
    k.box((L * 0.275 + 0.05, 0.58, -0.035), (math.hypot(L * 0.55, 0.9), 0.09, 0.05), OAK_DK, tilt=('z', -ang))   # diagonal brace (rises towards the far stile)
    if broken: k.box((L * 0.6, 0.45, -0.03), (0.35, 0.06, 0.05), OAK, tilt=('z', 35))                          # hanging splinter
    for y in (0.14, 1.06):
        if broken and y == 1.06: continue
        k.box((0.22, y, 0.04), (0.42, 0.045, 0.012), IRON)                                    # strap hinges
    k.box((L - 0.06, 0.90, 0.05), (0.08, 0.06, 0.02), IRON)                                  # spring catch
    return k.link(pm)


# The pivot: at the hinge axis, rotation.y = 90 (leaf runs from the hanging post to the slam post, local -Z). Swinging the
# intact leaf open into the pasture is rotation.y towards 0 (fully open at 0).
pivot = empty("LWF_FieldGate_LeafPivot", HINGE, 90.0)
intact = leaf_kit("LWF_FieldGate_LeafIntact", False); intact.parent = pivot; blender_xf(intact, Matrix.Translation(G(0, 0.02, 0)))
# Broken: hanging off the bottom hinge, swung 55 degrees into the pasture, far end on the ground. Baked under the pivot, so it
# shows correctly with the pivot at its closed yaw (90).
broken = leaf_kit("LWF_FieldGate_LeafBroken", True); broken.parent = pivot
blender_xf(broken, Matrix.Translation(G(0, 0.30, 0)) @ Matrix.Rotation(math.radians(-55), 4, 'Z') @ Matrix.Rotation(math.radians(6), 4, 'Y'))
# Repaired: the broken leaf hauled back shut, sagging a little.
repaired = leaf_kit("LWF_FieldGate_LeafRepaired", True); repaired.parent = pivot
blender_xf(repaired, Matrix.Translation(G(0, 0.06, 0)) @ Matrix.Rotation(math.radians(1.8), 4, 'Y'))
# The repair: orange baler twine lashing the leaf to the slam post, and a pallet wired across the gap on the site side.
k = Kit("LWF_FieldGate_Repair")
for y in (0.35, 0.75, 1.0):
    pts = [(0.13 * math.cos(t), y + 0.03 * math.sin(3 * t), SZ + 0.05 + 0.13 * math.sin(t)) for t in [i * math.pi / 4 for i in range(9)]]
    for a, b in zip(pts, pts[1:]): k.bar(a, b, 0.012, TWINE)
pal = Kit("pal")
for i in range(5): pal.box((0.0, 0.55, -0.5 + i * 0.25), (0.025, 1.05, 0.14), PALLET)
for y in (0.12, 0.55, 0.98): pal.box((-0.06, y, 0.0), (0.09, 0.09, 1.1), PALLET_DK)
po = pal.link(pm); blender_xf(po, Matrix.Translation(G(-0.12, 0, -0.1)) @ Matrix.Rotation(math.radians(-10), 4, 'Y'))
for y in (0.4, 0.9): k.bar((-0.06, y, -0.6), (-0.06, y, 0.4), 0.01, TWINE)
repair = k.link(pm)
bm_ = bmesh.new(); bm_.from_mesh(repair.data); bm_.from_mesh(po.data); bm_.to_mesh(repair.data); bm_.free(); bpy.data.objects.remove(po)
export([posts, pivot, repair], "lwf_field_gate_oak_v1",
       origin="hedge opening centre on the ground; gate line along local Z; pasture +X, site -X",
       pivot_node="LWF_FieldGate_LeafPivot", pivot_local=list(HINGE), closed_rotation_y=90.0, open_rotation_y=0.0,
       states={"intact": "show LeafIntact; hide LeafBroken, LeafRepaired, Repair; pivot yaw 90 shut .. 0 open",
               "broken": "show LeafBroken only (pivot at 90); the gap is open",
               "repaired": "show LeafRepaired and Repair (pivot at 90)"})

# ================================================================ 2. pasture pieces
# Ground dressing and fence: origin at the pasture centre, world (45.2, 0, -2). Trough and feeder: origin at the base centre.
PC = (45.2, -2.0); X0, X1, Z0, Z1 = 32.4 - PC[0], 58.0 - PC[0], -26.0 - PC[1], 22.0 - PC[1]
reset(); pm = palette_mat(); rng = random.Random(5)
k = Kit("LWF_CowPasture_Ground")
k.flat([(X0, Z0), (X1, Z0), (X1, Z1), (X0, Z1)], -0.018, PASTURE)
for i in range(24):                                                   # grazed and worn patches, each at its own height
    cx, cz = rng.uniform(X0 + 2, X1 - 2), rng.uniform(Z0 + 2, Z1 - 2)
    k.cyl((cx, -0.017 + 0.0006 * i, cz), rng.uniform(1.0, 3.0), 0.0005, rng.choice((GRAZED, GRAZED, WORN)), seg=9)
for i, (gx, gz, gr) in enumerate(((33.2, -2.7, 1.2), (34.4, -2.2, 0.9), (34.1, -3.5, 0.8), (35.4, -2.9, 0.6))):   # muddy gateway
    k.cyl((gx - PC[0], -0.002 + 0.0006 * i, gz - PC[1]), gr, 0.0005, MUD, seg=9)
for _ in range(22):                                                   # cowpats
    k.cyl((rng.uniform(X0 + 1, X1 - 1), -0.004, rng.uniform(Z0 + 1, Z1 - 1)), rng.uniform(0.13, 0.2), 0.03, PAT, seg=6)
for _ in range(16):                                                   # thistles
    x, z = rng.uniform(X0 + 1, X1 - 1), rng.uniform(Z0 + 1, Z1 - 1); h = rng.uniform(0.35, 0.6)
    k.cyl((x, 0, z), 0.02, h, LEAF, seg=4); k.cyl((x, h, z), 0.07, 0.09, THISTLE, seg=6, r2=0.03)
    for a in range(0, 360, 120): k.box((x + 0.08 * math.cos(math.radians(a)), 0.06, z + 0.08 * math.sin(math.radians(a))), (0.16, 0.02, 0.05), LEAF, yaw=-a)
ground = k.link(pm, recalc=False)
export([ground], "lwf_cow_pasture_ground_v1", origin="pasture centre, world (45.2, 0, -2)", extent_world=dict(x=[32.4, 58.0], z=[-26.0, 22.0]),
       heights="base y -0.018 (above FarmSurround's -0.025); patches stepped up from it")

reset(); pm = palette_mat()
k = Kit("LWF_CowPasture_Fence")
def rails(a, b, n):
    A, B = V(a), V(b)
    for i in range(n + 1):
        p = A.lerp(B, i / n); k.box((p.x, 0.6, p.y), (0.14, 1.2, 0.14), FENCE)
    for y in (0.45, 0.85, 1.12): k.bar((A.x, y, A.y), (B.x, y, B.y), 0.04, FENCE)
rails((X1, Z0), (X1, Z1), 18); rails((X0 + 0.6, Z0), (X1, Z0), 9); rails((X0 + 0.6, Z1), (X1, Z1), 9)
fence = k.link(pm)
export([fence], "lwf_cow_pasture_fence_v1", origin="pasture centre, world (45.2, 0, -2)", note="post-and-rail on the three outer sides; the hedge is the fourth")

reset(); pm = palette_mat()
k = Kit("LWF_WaterTrough")
k.box((0, 0.3, 0), (0.65, 0.6, 2.3), GALV); k.box((0, 0.55, 0), (0.52, 0.06, 2.15), WATER)
for z in (-0.9, 0.9): k.box((0, 0.04, z), (0.75, 0.08, 0.12), GALV_DK)
trough = k.link(pm)
export([trough], "lwf_water_trough_v1", origin="base centre; long axis along local Z", size_m=[0.65, 0.6, 2.3])

reset(); pm = palette_mat()
k = Kit("LWF_HayRingFeeder")
k.cyl((0, 0.1, 0), 0.95, 0.9, HAY, seg=10)
for i in range(16):
    a = 2 * math.pi * i / 16; k.box((1.0 * math.cos(a), 0.55, 1.0 * math.sin(a)), (0.05, 1.1, 0.05), GALV_DK)
for y in (0.1, 1.08):
    ring = [(1.0 * math.cos(2 * math.pi * i / 16), y, 1.0 * math.sin(2 * math.pi * i / 16)) for i in range(17)]
    for a, b in zip(ring, ring[1:]): k.bar(a, b, 0.03, GALV)
feeder = k.link(pm)
export([feeder], "lwf_hay_ring_feeder_v1", origin="base centre", diameter_m=2.06)

# ================================================================ 3. power leads (intact and chewed), spark anchor
# Origin: where the lead leaves the equipment, on the ground. The lead runs out along local +X with a gentle curve.
LEAD = [(0.0, 0.22, 0.0), (0.15, 0.03, 0.08), (0.9, 0.03, 0.42), (1.7, 0.03, 0.62), (2.3, 0.03, 0.55)]
reset(); pm = palette_mat()
k = Kit("LWF_PowerLead")
full = LEAD[:-1] + [(2.3, 0.03, 0.55), (3.1, 0.03, 0.9), (3.9, 0.03, 1.6)]
for a, b in zip(full, full[1:]): k.bar(a, b, 0.022, CABLE, seg=5)
k.box((3.95, 0.05, 1.65), (0.12, 0.08, 0.08), CABLE, yaw=-40)                                # plug at the far end
lead = k.link(pm)
export([lead], "lwf_power_lead_v1", origin="where the lead leaves the equipment, on the ground; runs along local +X")

reset(); pm = palette_mat(); sm = spark_mat(); rng = random.Random(3)
k = Kit("LWF_PowerLead_Chewed")
for a, b in zip(LEAD, LEAD[1:]): k.bar(a, b, 0.022, CABLE, seg=5)
tail = [(2.75, 0.03, 0.78), (3.5, 0.03, 1.2), (3.9, 0.03, 1.8)]
for a, b in zip(tail, tail[1:]): k.bar(a, b, 0.022, CABLE, seg=5)
def frayed(at, heading, n):
    for i in range(n):
        a = heading + math.radians(-40 + 80 * i / (n - 1)); L_ = rng.uniform(0.07, 0.13)
        k.bar((at[0], 0.03, at[1]), (at[0] + L_ * math.cos(a), 0.03 + rng.uniform(0, 0.05), at[1] + L_ * math.sin(a)), 0.005, COPPER, seg=3)
frayed((2.32, 0.55), math.radians(-10), 6); frayed((2.73, 0.77), math.radians(200), 4)
k.cyl((2.45, -0.002, 0.62), 0.28, 0.004, SCORCH, seg=10)
chewed = k.link(pm)
SPARK = (2.45, 0.12, 0.62)
anchor = empty("LWF_Spark_Anchor", SPARK)
ks = Kit("LWF_Spark")
for i in range(8):
    a = i * math.pi / 4; L_ = 0.22 * (1.0 if i % 2 == 0 else 0.55)
    ks.bar((0, 0, 0), (L_ * math.cos(a) * 0.7, L_ * 0.6 * math.sin(a + 0.6) + 0.02, L_ * math.sin(a) * 0.7), 0.012, 0, seg=3)
ks.ball((0, 0, 0), 0.045, 0, sub=1)
spark = ks.link(sm); spark.parent = anchor
export([chewed, anchor], "lwf_power_lead_chewed_v1", origin="where the lead leaves the equipment, on the ground; runs along local +X",
       spark_anchor="LWF_Spark_Anchor at local (2.45, 0.12, 0.62); its child LWF_Spark (emissive) is the flash to flicker")

# ================================================================ 4. layout
LAYOUT = dict(
    note="Godot world metres; yaw degrees about +Y. Built by build_cow_pasture.py.",
    hedge_remove=[dict(file="lwf_hedge_straight_8m_d_v1.glb", x=32.0, z=0.0, yaw=90, why="Main.BuildHedgeBoundary east run i = 0 (Hedge(i + 7) = 'd'), covers z 0 .. -8")],
    hedge_add=[dict(file="lwf_hedge_gate_end_v1.glb", x=32.0, z=0.0, yaw=90, covers_z=[0.0, -1.0]),
               dict(file="lwf_hedge_gate_end_v1.glb", x=32.0, z=-5.3, yaw=270, covers_z=[-5.4, -4.3]),
               dict(file="lwf_hedge_straight_4m_a_v1.glb", x=32.0, z=-4.6, yaw=90, covers_z=[-4.6, -8.6])],
    opening_z=[-1.0, -4.3],
    gate=dict(file="lwf_field_gate_oak_v1.glb", x=32.0, z=-2.65, yaw=0, pivot_node="LWF_FieldGate_LeafPivot",
              closed_rotation_y=90.0, open_rotation_y=0.0,
              leaves=dict(intact="LWF_FieldGate_LeafIntact", broken="LWF_FieldGate_LeafBroken", repaired="LWF_FieldGate_LeafRepaired"),
              repair_node="LWF_FieldGate_Repair"),
    pasture=dict(ground=dict(file="lwf_cow_pasture_ground_v1.glb", x=45.2, z=-2.0, yaw=0),
                 fence=dict(file="lwf_cow_pasture_fence_v1.glb", x=45.2, z=-2.0, yaw=0),
                 trough=dict(file="lwf_water_trough_v1.glb", x=35.5, z=-6.6, yaw=0),
                 feeder=dict(file="lwf_hay_ring_feeder_v1.glb", x=44.0, z=6.0, yaw=0),
                 cows_suggested=[dict(x=x, z=z, yaw=yaw) for x, z, yaw in ((38.5, -3.0, 200), (36.4, -7.8, 105), (41.0, -9.5, 30),
                     (44.5, -2.5, 260), (40.0, 4.5, 150), (47.5, 9.5, 320), (43.0, 11.5, 80), (50.5, -6.0, 190), (52.0, 3.5, 10), (46.0, -14.0, 120))]),
    cable=dict(intact="lwf_power_lead_v1.glb", chewed="lwf_power_lead_chewed_v1.glb", spark_anchor="LWF_Spark_Anchor", spark_node="LWF_Spark",
               icon="game/assets/ui/field-notes/pin_chewed_cable.png", example=dict(at="hire generator socket side", x=28.1, z=-5.8, yaw=0)))
json.dump(LAYOUT, open(os.path.join(OUT, "cow_pasture_layout_v1.json"), "w"), indent=1)
json.dump(REPORT, open(os.path.join(OUT, "cow_pasture_report.json"), "w"), indent=1)
print("DONE")
