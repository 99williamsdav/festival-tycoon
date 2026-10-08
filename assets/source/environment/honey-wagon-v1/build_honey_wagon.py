# Dav's Lav-Sucker production (approved: livery B "Cheeky" with "No jobbie too big", the rear face, the rear sign; sign A; hose).
# Board: Documents/Festival Tycoon concepts/honey-wagon/.
#   python make_textures.py ; blender -b --python build_honey_wagon.py -- <out_dir>
# Godot axes in the code and JSON (Blender (x, y, z) = Godot (x, -z, y)).
import bpy, bmesh, math, sys, os, json, hashlib, random
from mathutils import Vector as V, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(argv[0]) if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
REPORT = {}


def G(x, y, z): return V((x, -z, y))
def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)


PAL = ["8bbd3c", "3d7f2a", "f4f2ea", "2a2c2e", "1d1f21", "a9aeb0", "34434e", "c9cfd4", "b8913a", "c0392b", "2f3b30", "1d2620",
       "f2a33a", "808080", "808080", "808080"]
LIME, GREEN, WHITE, DARK, TYRE, RIM, GLASS, CHROME, BRASS, RED, HOSE, HOSE_RIB, AMBER = range(13)
PW = 8 * len(PAL)
def U(slot): return ((8 * slot + 4) / PW, 0.5)


def palette_mat():
    m = bpy.data.materials.new("LWF_HoneyWagon_MattePalette"); m.use_nodes = True
    img = bpy.data.images.new("lwf_honey_wagon_palette", PW, 8); px = []
    for y in range(8):
        for hx in PAL: px += ([int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.7; b.inputs["Specular IOR Level"].default_value = 0.35
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); return m


def export(objs, name, **meta):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    meshes = [o for o in objs if o.type == 'MESH']
    REPORT[name] = dict(file=name + ".glb", nodes=sorted(o.name for o in objs), triangles=sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons),
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
    def rod(self, a, b, r, slot, seg=8):
        """a round bar of exactly |b - a| (no overlap allowance), for large radii: the tank, bands, wheels"""
        a, b = G(*a), G(*b); d = b - a
        M = Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=M)["verts"], U(slot))
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






def ellip(k, c, radii, slot, sub=2):
    """ellipsoid at Godot c with Godot radii (rx, ry, rz)"""
    M = Matrix.Translation(G(*c)) @ Matrix.Diagonal((radii[0], radii[2], radii[1], 1))
    k._paint(bmesh.ops.create_icosphere(k.bm, subdivisions=sub, radius=1.0, matrix=M)["verts"], U(slot))


def tex_mat(name, png, clip=False, rough=0.75):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(OUT, png))
    m.node_tree.links.new(tx.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = rough
    if clip:
        m.node_tree.links.new(tx.outputs["Alpha"], b.inputs["Alpha"]); m.blend_method = 'CLIP'; m.alpha_threshold = 0.5
    return m


def uv_mesh(name, verts, faces, uvs, mat):
    """verts in Godot coords; uvs per face corner (list of lists)"""
    me = bpy.data.meshes.new(name); me.from_pydata([G(*v) for v in verts], [], faces)
    uvl = me.uv_layers.new(name="UVMap")
    for f, fuv in zip(me.polygons, uvs):
        for li, uv in zip(f.loop_indices, fuv): uvl.data[li].uv = uv
    me.materials.append(mat); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o


def empty(name, at=(0, 0, 0)):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e); e.location = G(*at); return e


# ================================================================ 1. the truck: lwf_honey_wagon_v1
# Origin: ground centre of the footprint. Forward is Godot -Z (cab at -Z, tank and rear at +Z); right is +X.
reset(); pm = palette_mat()
k = Kit("LWF_HoneyWagon_Body")
k.box((0, 0.62, 0), (0.9, 0.2, 4.9), DARK)                                                        # chassis
for sx in (-1, 1): k.box((sx * 0.84, 0.92, 1.45), (0.34, 0.06, 1.05), DARK)                        # rear mudguards
k.box((0, 1.45, -1.75), (2.0, 1.75, 1.4), LIME)                                                   # cab-over cab
k.box((0, 1.78, -2.455), (1.78, 0.72, 0.03), GLASS)                                               # windscreen
for sx in (-1, 1):
    k.box((sx * 1.005, 1.82, -1.85), (0.02, 0.6, 0.9), GLASS)                                     # side windows
    k.box((sx * 0.72, 0.86, -2.48), (0.28, 0.16, 0.04), CHROME)                                   # headlights
    k.bar((sx * 1.0, 1.95, -2.3), (sx * 1.12, 1.95, -2.42), 0.018, DARK); k.bar((sx * 1.12, 1.95, -2.42), (sx * 1.12, 1.6, -2.42), 0.018, DARK)
    k.box((sx * 1.12, 1.62, -2.42), (0.08, 0.26, 0.05), DARK)                                     # mirrors
    k.box((sx * 1.002, 1.18, -1.75), (0.02, 0.28, 1.4), GREEN)                                    # cab stripe, sides
k.box((0, 1.18, -2.462), (1.6, 0.28, 0.02), GREEN)                                                # cab stripe, front
k.box((0, 0.56, -2.49), (2.0, 0.3, 0.12), DARK)                                                   # bumper and grille
k.box((0, 2.345, -1.55), (0.9, 0.05, 0.2), DARK)                                                  # beacon bar
TZ0, TZ1, TY, TR = -0.45, 2.2, 1.75, 0.8
k.rod((0, TY, TZ0), (0, TY, TZ1), TR, WHITE, seg=16)                                              # the tank barrel
ellip(k, (0, TY, TZ0), (TR, TR, 0.256), WHITE)                                                    # front dished end
for z in (TZ1 - 0.3, (TZ0 + TZ1) / 2, TZ0 + 0.25): k.rod((0, TY, z - 0.045), (0, TY, z + 0.045), TR + 0.012, GREEN, seg=16)   # bands
for z in (1.9, 0.9, -0.2): k.box((0, 1.0, z), (1.3, 0.3, 0.2), DARK)                              # cradles
k.cyl((0, 2.52, 1.0), 0.18, 0.12, DARK, seg=10)                                                   # manlid
for sx in (-1, 1): k.rod((sx * 0.98, 0.86, -0.3), (sx * 0.98, 0.86, 2.1), 0.075, HOSE, seg=8)     # hose storage tubes
for z in (1.85, 2.12): k.bar((0.92, 0.7, z), (0.92, 2.05, z), 0.018, CHROME)                      # ladder (right side, rear)
for i in range(6): k.bar((0.92, 0.82 + i * 0.22, 1.85), (0.92, 0.82 + i * 0.22, 2.12), 0.014, CHROME)
k.rod((0, 1.45, 2.38), (0, 1.45, 2.70), 0.12, BRASS, seg=10)                                      # rear valve: the face's nose
k.box((0.17, 1.58, 2.62), (0.05, 0.3, 0.05), RED)                                                 # valve lever
body = k.link(pm)
wheels = []
for nm, x, z in (("FL", -0.84, -1.55), ("FR", 0.84, -1.55), ("RL", -0.84, 1.45), ("RR", 0.84, 1.45)):
    k = Kit(f"LWF_HoneyWagon_Wheel_{nm}")
    k.rod((-0.15, 0, 0), (0.15, 0, 0), 0.42, TYRE, seg=12); k.rod((-0.16, 0, 0), (0.16, 0, 0), 0.22, RIM, seg=8)
    wheels.append(k.link(pm, at=(x, 0.42, z)))
beacon_mat = bpy.data.materials.new("LWF_HoneyWagon_BeaconLamp"); beacon_mat.use_nodes = True
bb = beacon_mat.node_tree.nodes["Principled BSDF"]; bb.inputs["Base Color"].default_value = (0.89, 0.39, 0.03, 1)
bb.inputs["Emission Color"].default_value = (1.0, 0.45, 0.05, 1); bb.inputs["Emission Strength"].default_value = 0.4
k = Kit("LWF_HoneyWagon_Beacon"); k.cyl((0, 0.0, 0), 0.11, 0.13, AMBER, seg=10); beacon = k.link(beacon_mat, at=(0, 2.37, -1.55))
# rear dish with the face: a half-ellipsoid cap, planar UVs (u = 0.5 + x/1.6, v = 0.5 + (y - 1.75)/1.6)
face_m = tex_mat("LWF_HoneyWagon_RearFace", "lwf_honey_wagon_rear_face_v1.png", rough=0.6)
NR, NS = 6, 24; verts, faces, uvs = [], [], []
for i in range(NR + 1):
    phi = math.radians(90 * i / NR)
    for j in range(NS):
        t = 2 * math.pi * j / NS; x = TR * math.sin(phi) * math.cos(t); y = TY + TR * math.sin(phi) * math.sin(t); z = TZ1 + 0.256 * math.cos(phi)
        verts.append((x, y, z))
def UVp(v): return (0.5 + v[0] / 1.6, 0.5 + (v[1] - TY) / 1.6)
for i in range(NR):
    for j in range(NS):
        a, b, c, dd = i * NS + j, i * NS + (j + 1) % NS, (i + 1) * NS + (j + 1) % NS, (i + 1) * NS + j
        f = (a, dd, c, b); faces.append(f); uvs.append([UVp(verts[q]) for q in f])
face = uv_mesh("LWF_HoneyWagon_RearFace", verts, faces, uvs, face_m)
bm_ = bmesh.new(); bm_.from_mesh(face.data); bmesh.ops.remove_doubles(bm_, verts=bm_.verts, dist=1e-6); bm_.to_mesh(face.data); bm_.free()
face.data.update()
# name boards: strips curved to the tank side, angles -24..24 deg about the barrel, z from -0.25 to 1.85
board_m = tex_mat("LWF_HoneyWagon_NameBoard", "lwf_honey_wagon_name_board_v1.png", rough=0.6)
boards = []
for side, nm in ((1, "R"), (-1, "L")):
    n = 8; verts, faces, uvs = [], [], []
    for i in range(n + 1):
        a = math.radians(-24 + 48 * i / n)
        for z in (-0.25, 1.85): verts.append((side * (TR + 0.016) * math.cos(a), TY + (TR + 0.016) * math.sin(a), z))
    for i in range(n):
        q = (2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) if side < 0 else (2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1)
        faces.append(q)
        # u runs to the viewer's right: on the right side (+X) that is towards -Z (the cab); on the left towards +Z
        uvs.append([((1.0 if verts[v][2] < 0 else 0.0) if side > 0 else (0.0 if verts[v][2] < 0 else 1.0), (v // 2) / n) for v in q])
    boards.append(uv_mesh(f"LWF_HoneyWagon_NameBoard_{nm}", verts, faces, uvs, board_m))
plate_m = tex_mat("LWF_HoneyWagon_RearPlate", "lwf_honey_wagon_rear_plate_v1.png", rough=0.7)
plate = uv_mesh("LWF_HoneyWagon_RearPlate", [(-0.8, 0.52, 2.48), (0.8, 0.52, 2.48), (0.8, 0.72, 2.48), (-0.8, 0.72, 2.48)], [(0, 1, 2, 3)],
                [[(0, 0), (1, 0), (1, 1), (0, 1)]], plate_m)
port = empty("LWF_HoneyWagon_HosePort", (0, 1.45, 2.72))
objs = [body] + wheels + [beacon, face] + boards + [plate, port]
export(objs, "lwf_honey_wagon_v1", origin="ground centre of the footprint; forward Godot -Z (cab), rear +Z", size_m=[2.0, 2.6, 5.2],
       named_nodes=dict(body="LWF_HoneyWagon_Body", wheels=["LWF_HoneyWagon_Wheel_FL", "LWF_HoneyWagon_Wheel_FR", "LWF_HoneyWagon_Wheel_RL", "LWF_HoneyWagon_Wheel_RR"],
                  beacon="LWF_HoneyWagon_Beacon (material LWF_HoneyWagon_BeaconLamp; raise its emission to flash)",
                  hose_port="LWF_HoneyWagon_HosePort at (0, 1.45, 2.72): the end of the rear valve, facing +Z",
                  face="LWF_HoneyWagon_RearFace", name_boards=["LWF_HoneyWagon_NameBoard_L", "LWF_HoneyWagon_NameBoard_R"], rear_plate="LWF_HoneyWagon_RearPlate"),
       wheel_radius_m=0.42)

# ================================================================ 2. the hose: lwf_honey_wagon_hose_v1 (portaloo-local frame)
# Authored in the PORTALOO's own frame (the door faces local -Z). Place it at the portaloo's transform (or as its child).
# The truck goes at portaloo-local (-3.4, 0, -0.9), yaw 180, so its hose port lands on the hose's first point.
reset(); pm = palette_mat()
PATH = [(-3.4, 1.45, -3.62), (-3.4, 1.2, -3.78), (-3.38, 0.6, -3.92), (-3.25, 0.06, -4.4), (-2.3, 0.06, -4.6), (-1.5, 0.06, -3.6),
        (-0.95, 0.06, -2.6), (-0.6, 0.08, -1.62), (-0.52, 0.13, -0.92)]
def catmull(pts, per=8):
    P = [V(p) for p in pts]; P = [P[0]] + P + [P[-1]]; out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for s in range(per):
            t = s / per; t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-2]); return out
dense = catmull(PATH)
L_ = [0.0]
for a, b in zip(dense, dense[1:]): L_.append(L_[-1] + (b - a).length)
def at(s):
    for i in range(len(dense) - 1):
        if L_[i + 1] >= s:
            t = (s - L_[i]) / max(1e-6, L_[i + 1] - L_[i]); return dense[i].lerp(dense[i + 1], t)
    return dense[-1]
SEG = 0.17; NSEG = max(2, round(L_[-1] / SEG)); SL = L_[-1] / NSEG
segs = []
for i in range(NSEG):
    a, b = at(i * SL), at((i + 1) * SL); c = (a + b) / 2; dvec = (b - a)
    k = Kit(f"LWF_Hose_Seg_{i:02d}")
    Lh = dvec.length / 2 + 0.008
    k.bar((0, -Lh, 0), (0, Lh, 0), 0.055, HOSE, seg=8); k.bar((0, -0.015, 0), (0, 0.015, 0), 0.062, HOSE_RIB, seg=8)
    o = k.link(pm)
    # orient: the segment's local +Y (Godot up = Blender +Z) along the hose
    o.matrix_world = Matrix.Translation(G(*c)) @ G(*dvec).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
    segs.append(o)
k = Kit("LWF_Hose_Coupling"); k.rod((0, 0, 0), (0, 0, 0.08), 0.075, BRASS, seg=10); coup = k.link(pm, at=(-0.52, 0.13, -0.92))
k = Kit("LWF_Hose_PortCoupling"); k.rod((0, 0, 0), (0, 0, -0.06), 0.075, BRASS, seg=10); pc = k.link(pm, at=(-3.4, 1.45, -3.62))
export(segs + [coup, pc], "lwf_honey_wagon_hose_v1", origin="the portaloo's origin; authored in the portaloo frame (door faces -Z)",
       segments=NSEG, segment_length_m=round(SL, 4), length_m=round(L_[-1], 3),
       segment_nodes=f"LWF_Hose_Seg_00 (at the truck) .. LWF_Hose_Seg_{NSEG - 1:02d} (at the loo); each pivots at its own centre with local +Y along the hose, so scaling local X and Z bulges it",
       outlet="LWF_Hose_Coupling on the door face at (-0.52, 0.13, -0.92); LWF_Hose_PortCoupling at the truck end (-3.4, 1.45, -3.62)")

# ================================================================ 3. the sign: lwf_portaloo_out_of_order_sign_v1
reset()
sm = tex_mat("LWF_OutOfOrderSign", "lwf_portaloo_out_of_order_sign_v1.png", clip=True, rough=0.9)
w, h = 0.72, 0.52; tl = math.radians(7)
def sp(u, v): return (u * math.cos(tl) - v * math.sin(tl), u * math.sin(tl) + v * math.cos(tl), 0.0)
# faces Godot -Z (the door's outside): viewed from -Z, the viewer's right is -X
sign = uv_mesh("LWF_OutOfOrderSign", [sp(w / 2, -h / 2), sp(-w / 2, -h / 2), sp(-w / 2, h / 2), sp(w / 2, h / 2)], [(0, 1, 2, 3)],
               [[(0, 0), (1, 0), (1, 1), (0, 1)]], sm)
export([sign], "lwf_portaloo_out_of_order_sign_v1", origin="sign centre; faces Godot -Z; 7 degree tilt baked in", size_m=[w, h],
       parent="portaloo node DoorPivot, local position (-0.45, 1.34, -0.04): 1.5 cm proud of the door leaf, so it swings with the door")

# ================================================================ 4. layout (all relative to the portaloo being emptied)
LAYOUT = dict(note="Godot metres and degrees, in the portaloo's local frame (lwf_portaloo_v1: door faces local -Z, DoorPivot at (0.45, 0.08, -0.795)).",
              truck=dict(file="lwf_honey_wagon_v1.glb", local=[-3.4, 0.0, -0.9], yaw=180, hose_port="LWF_HoneyWagon_HosePort", beacon="LWF_HoneyWagon_Beacon"),
              hose=dict(file="lwf_honey_wagon_hose_v1.glb", local=[0, 0, 0], yaw=0, segments=NSEG, order="Seg_00 at the truck .. last at the loo",
                        bulge_suggestion="scale local X and Z of consecutive segments to ~1.9 in a travelling wave from the loo end to the truck end, one slug every ~2 s, each passing over ~0.6 s"),
              sign=dict(file="lwf_portaloo_out_of_order_sign_v1.glb", parent="DoorPivot", local=[-0.45, 1.34, -0.04], yaw=0),
              indicator="swap the portaloo's indicator to lwf_portaloo_indicator_occupied_v1 while the sign is up",
              approach="the truck drives in down the entrance lane (x = 0) and parks with its rear by the loo's door side")
json.dump(LAYOUT, open(os.path.join(OUT, "honey_wagon_layout_v1.json"), "w"), indent=1)
json.dump(REPORT, open(os.path.join(OUT, "honey_wagon_report.json"), "w"), indent=1)
print("DONE")
