# Gate apron + three gate-sign tiers, v1.
# blender -b --python build_gate_sign_assets.py -- <out_dir>
# Run make_textures.py (system Python + Pillow) first. Writes:
#   lwf_gate_apron_v1.glb, lwf_gate_sign_tier1_v1.glb, lwf_gate_sign_tier2_v1.glb, lwf_gate_sign_tier3_v1.glb,
#   a .blend per asset, and lettering_areas.json.
# Coordinates: every asset's origin is the gate centre line on the ground at the hedge line, i.e. place each root at
# Godot (0, 0, 32) with no rotation. Godot local +Z points away from the farm (down the lane); +X is the farm's +X.
# In Blender that is -Y outward (glTF export converts Blender +Z up / -Y forward to Godot +Y up / +Z).
import bpy, bmesh, math, random, sys, os, json
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)
TEX = os.path.join(HERE, "tex")

def U(slot):  # palette swatch centre
    return ((8 * slot + 4) / 96.0, 0.5)
GRASS, GRASS_D, GRASS_L, LANE, LANE_D, SOIL, WOOD, DARK, GREEN, GOLD, TERRA, CREAM = range(12)

def T(v): return Matrix.Translation(Vector(v))
def RZ(a): return Matrix.Rotation(a, 4, 'Z')
def RY(a): return Matrix.Rotation(a, 4, 'Y')
def RX(a): return Matrix.Rotation(a, 4, 'X')

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def palette_material():
    m = bpy.data.materials.new("LWF_GateSign_MattePalette"); m.use_nodes = True; nt = m.node_tree
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(TEX, "gate_sign_palette.png"))
    tx.interpolation = 'Closest'
    b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9
    nt.links.new(tx.outputs[0], b.inputs["Base Color"]); return m

def art_material(name, file):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(TEX, file)); tx.interpolation = 'Linear'
    b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9
    nt.links.new(tx.outputs[0], b.inputs["Base Color"]); return m

class Mesh:
    """bmesh builder with a UV layer; palette faces get a swatch-centre UV, art faces a 0..1 quad."""
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def _swatch(self, faces, slot, mat=0):
        for f in faces:
            f.material_index = mat
            for l in f.loops:
                l[self.uv].uv = U(slot)
    def _res_faces(self, res):
        return list({f for v in res["verts"] for f in v.link_faces})
    def box(self, M, size, slot):
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=M @ Matrix.Diagonal((*size, 1))); self._swatch(self._res_faces(r), slot)
    def cyl(self, M, r1, r2, depth, slot, seg=8, caps=True):
        r = bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=seg, radius1=r1, radius2=r2, depth=depth, matrix=M); self._swatch(self._res_faces(r), slot)
    def ball(self, M, r, slot, sub=1):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=r, matrix=M); self._swatch(self._res_faces(res), slot)
    def tri(self, a, b, c, slot):
        f = self.bm.faces.new([self.bm.verts.new(p) for p in (a, b, c)]); self._swatch([f], slot); return f
    def quad(self, pts, slot):
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts]); self._swatch([f], slot); return f
    def art_quad(self, M, w, h, mat):
        """board face in local XZ plane facing local -Y; UV 0..1 left-to-right, bottom-to-top."""
        cs = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
        vs = [self.bm.verts.new(M @ Vector((x, 0, z))) for x, z in cs]
        f = self.bm.faces.new(vs); f.material_index = mat
        for l, (u, v) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            l[self.uv].uv = (u, v)
        return f
    def link(self, mats):
        bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces])
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o

AREAS = {}
def lettering_area(asset, name, M, w, h):
    """Empty at the centre of the lettering rectangle, 2 mm proud of the board. Its Blender local -Y is the board's
    outward normal, so after glTF export the Godot node's local +Z faces the reader, +X is text-right, +Y is up."""
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'PLAIN_AXES'
    e.matrix_world = M @ T((0, -0.002, 0)); bpy.context.scene.collection.objects.link(e)
    e["width_m"] = round(w, 3); e["height_m"] = round(h, 3)
    p = e.matrix_world.to_translation(); r = e.matrix_world.to_3x3()
    def gd(v): return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]
    AREAS.setdefault(asset, {})[name] = {"node": name, "centre_godot_local": gd(p), "size_m": [round(w, 3), round(h, 3)],
                                        "facing_godot": gd(r @ Vector((0, -1, 0))), "text_right_godot": gd(r @ Vector((1, 0, 0))),
                                        "up_godot": gd(r @ Vector((0, 0, 1)))}

def board(meshes, M, w, h, thick, edge_slot, art_mat_index, area=None):
    """a board: palette edge box behind, art face on the front (local -Y)."""
    frame, face = meshes
    frame.box(M @ T((0, thick / 2, 0)), (w + 0.03, thick, h + 0.03), edge_slot)
    face.art_quad(M @ T((0, -0.003, 0)), w, h, art_mat_index)
    if area:
        asset, name, cx, cz, aw, ah = area
        lettering_area(asset, name, M @ T((cx, -0.003, cz)), aw, ah)

def pennants(mesh, a, b, n, size, sag, slots=(GOLD, GREEN, TERRA, CREAM)):
    a, b = Vector(a), Vector(b); side = (b - a).normalized()
    pts = []
    for i in range(n + 1):
        t = i / n; p = a.lerp(b, t); p.z -= sag * 4 * t * (1 - t); pts.append(p)
    for i, (p, q) in enumerate(zip(pts, pts[1:])):
        mesh.quad([p + Vector((0, 0, .012)), q + Vector((0, 0, .012)), q - Vector((0, 0, .012)), p - Vector((0, 0, .012))], DARK)
        m_ = p.lerp(q, 0.5); half = side * size * 0.45
        mesh.tri(m_ - half, m_ + half, m_ - Vector((0, 0, size * 1.1)), slots[i % len(slots)])

def export(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True,
                              export_animations=False, export_extras=True, export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons)
    print(f"EXPORTED {path} tris={tris}")

# ====================================================================== gate apron
# Uses the field/track palette itself (tex/field_track_palette.png, a copy of the grass tile's embedded palette) so the
# apron grass and lane are the same swatches as lwf_field_grass_tile_8m_v1 and lwf_vehicle_track_straight_8x4m_v1.
from mathutils.geometry import delaunay_2d_cdt
reset(); rng = random.Random(12)
FIELD = bpy.data.materials.new("LWF_FieldTrack_MattePalette"); FIELD.use_nodes = True
_tx = FIELD.node_tree.nodes.new("ShaderNodeTexImage"); _tx.image = bpy.data.images.load(os.path.join(TEX, "field_track_palette.png"))
_tx.interpolation = 'Closest'
_b = FIELD.node_tree.nodes["Principled BSDF"]; _b.inputs["Roughness"].default_value = 0.9
FIELD.node_tree.links.new(_tx.outputs[0], _b.inputs["Base Color"])
F_GRASS = (0, 1, 2, 3, 4, 5)          # the tile uses all six greens (counts 7/5/5/7/4/4 per 32 faces)
F_EDGE, F_MID, F_RUT, F_SOIL = 8, 9, 10, 11

HALF_W, DEPTH, BACK = 8.0, 9.0, 0.5           # 16 m wide, ~9 m beyond the hedge line, tucked 0.5 m under the hedge
def outline():
    pts = []
    for i in range(29):                      # from the +X back corner round the front to the -X back corner
        t = math.pi * i / 28
        c, s = math.cos(t), math.sin(t)
        sx = math.copysign(abs(c) ** 0.55, c); sy = abs(s) ** 0.6
        j = 1.0 + (rng.uniform(-0.07, 0.05) if 0 < i < 28 else 0.0)
        pts.append(Vector((HALF_W * sx * j, BACK - (DEPTH + BACK) * sy * j, 0.0)))
    return pts
OUTL = outline()

def inside(x, y):
    n = len(OUTL); c = False
    for i in range(n):
        a, b = OUTL[i], OUTL[(i + 1) % n]
        if (a.y > y) != (b.y > y) and x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x:
            c = not c
    return c

# grass: interior points on the field's 2 m grid (world-aligned: local x = Godot x, local z + 32 = Godot z), constrained
# Delaunay with the island outline, so facets are the tile's 2 m scale and the seam at the hedge lines up.
pts2 = [(p.x, p.y) for p in OUTL]
heights = [0.0] * len(OUTL)                  # outline at tile-edge height
for gx in range(-8, 9, 2):
    for gy in range(0, -12, -2):
        x, y = float(gx), float(gy)
        if inside(x, y) and min((Vector((x, y, 0)) - p).length for p in OUTL) > 0.9:
            pts2.append((x, y)); heights.append(rng.choice((0.0, 0.02, 0.03, 0.05)) if gy < 0 else 0.0)
edges = [(i, (i + 1) % len(OUTL)) for i in range(len(OUTL))]
vout, eout, fout, _, _, _ = delaunay_2d_cdt([Vector(p) for p in pts2], edges, [], 2, 1e-6)
hmap = {}
for i, v in enumerate(vout):
    best = min(range(len(pts2)), key=lambda k: (pts2[k][0] - v.x) ** 2 + (pts2[k][1] - v.y) ** 2)
    hmap[i] = heights[best]
ap = Mesh("GateApron")
for f in fout:
    vs = [Vector((vout[k].x, vout[k].y, hmap[k])) for k in f]
    for k in range(1, len(vs) - 1):
        ap.tri(vs[0].copy(), vs[k].copy(), vs[k + 1].copy(), rng.choice(F_GRASS))
# soil skirt with a slight inward taper so the island has a deliberate edge against the sky (kept from the first cut)
SKIRT = 0.45
CENTRE = Vector((0, -DEPTH * 0.42, 0))
for p, q in zip(OUTL[:-1], OUTL[1:]):
    pi = Vector((p.x * 0.97, CENTRE.y + (p.y - CENTRE.y) * 0.97, -SKIRT)); qi = Vector((q.x * 0.97, CENTRE.y + (q.y - CENTRE.y) * 0.97, -SKIRT))
    lp = Vector((p.x, p.y, -0.06)); lq = Vector((q.x, q.y, -0.06))
    ap.quad([Vector((p.x, p.y, 0.0)), lp, lq, Vector((q.x, q.y, 0.0))], 0)        # grass lip
    ap.quad([lp, pi, qi, lq], F_SOIL)

# the lane: the in-field track's exact cross-section (lwf_vehicle_track_straight_8x4m_v1), placed like the track
# (base y 0.052), running from under the hedge to the island edge.
lane = Mesh("Lane")
PROFILE = [(-2.0, 0.03), (-1.45, 0.03), (-1.05, 0.02), (-0.72, 0.02), (0.0, 0.03), (0.72, 0.02), (1.05, 0.02), (1.45, 0.03), (2.0, 0.03)]
BANDS = [F_EDGE, F_MID, F_RUT, F_MID, F_MID, F_RUT, F_MID, F_EDGE]
def front_y(x):
    best = min(OUTL[1:-1], key=lambda p: abs(p.x - x)); return best.y
LY0, LY1 = BACK, front_y(0.0) + 0.05
rows = [LY0 + (LY1 - LY0) * i / 7 for i in range(8)]
BASE = 0.052
for y0, y1 in zip(rows, rows[1:]):
    for (x0, h0), (x1, h1), slot in zip(PROFILE, PROFILE[1:], BANDS):
        a, b_, c, d_ = (Vector((x0, y0, BASE + h0)), Vector((x1, y0, BASE + h1)), Vector((x1, y1, BASE + h1)), Vector((x0, y1, BASE + h0)))
        lane.tri(a, b_, c, slot); lane.tri(a, c, d_, slot)
lane.quad([Vector((-2.0, LY1, BASE + 0.03)), Vector((2.0, LY1, BASE + 0.03)), Vector((2.0, LY1 - 0.02, -SKIRT * 0.8)), Vector((-2.0, LY1 - 0.02, -SKIRT * 0.8))], F_MID)
objs = [ap.link([FIELD]), lane.link([FIELD])]
export("lwf_gate_apron_v1", objs)

# ====================================================================== tier 1: the old door
reset(); PAL = palette_material()
DOOR = art_material("LWF_GateSign_Tier1_DoorArt", "t1_door.png"); ARROW = art_material("LWF_GateSign_Tier1_ArrowArt", "t1_arrow.png")
fr = Mesh("GateSignTier1"); face = Mesh("GateSignTier1_Board")
YAW = math.radians(32)
A = Vector((-5.4, -2.6, 0))
M = T(A + Vector((0, 0, 1.15))) @ RZ(YAW) @ RY(math.radians(-4))
board((fr, face), M, 2.5, 1.0, 0.045, WOOD, 0, ("tier1", "LetteringArea", 0.0, 0.0, 2.2, 0.78))
for o, h, lean in ((-1.0, 1.7, 5), (1.05, 1.5, -7)):
    fr.box(T(A + (RZ(YAW) @ Vector((o, 0.06, 0)))) @ RZ(YAW) @ RY(math.radians(lean)) @ T((0, 0, h / 2)), (0.08, 0.08, h), WOOD)
B = Vector((-3.4, -1.7, 0))
fr.box(T(B) @ RZ(YAW) @ RY(math.radians(6)) @ T((0, 0.04, 0.55)), (0.05, 0.05, 1.1), WOOD)
board((fr, face), T(B + Vector((0.05, 0, 1.0))) @ RZ(YAW + math.radians(8)) @ RY(math.radians(-6)), 0.9, 0.39, 0.012, WOOD, 1)
objs = [fr.link([PAL]), face.link([DOOR, ARROW])]
objs.append(bpy.data.objects["LetteringArea"])
export("lwf_gate_sign_tier1_v1", objs)

# ====================================================================== tier 2: painted board
reset(); PAL = palette_material(); ART = art_material("LWF_GateSign_Tier2_BoardArt", "t2_board.png")
fr = Mesh("GateSignTier2"); face = Mesh("GateSignTier2_Board"); pn = Mesh("Pennants")
A = Vector((-5.4, -2.6, 0))
M = T(A + Vector((0, 0, 1.65))) @ RZ(YAW)
board((fr, face), M, 3.3, 1.01, 0.07, DARK, 0, ("tier2", "LetteringArea", 0.31, 0.0, 2.48, 0.78))
for o in (-1.75, 1.75):
    fr.box(T(A + (RZ(YAW) @ Vector((o, 0.04, 0)))) @ RZ(YAW) @ T((0, 0, 1.35)), (0.13, 0.13, 2.7), WOOD)
    fr.box(T(A + (RZ(YAW) @ Vector((o, 0.04, 2.73)))) @ RZ(YAW), (0.19, 0.19, 0.06), DARK)
pennants(pn, A + (RZ(YAW) @ Vector((-1.75, -0.08, 2.62))), A + (RZ(YAW) @ Vector((1.75, -0.08, 2.62))), 9, 0.26, 0.10)
objs = [fr.link([PAL]), face.link([ART]), pn.link([PAL]), bpy.data.objects["LetteringArea"]]
export("lwf_gate_sign_tier2_v1", objs)

# ====================================================================== tier 3: the arch over the lane
reset(); PAL = palette_material()
ART = art_material("LWF_GateSign_Tier3_BoardArt", "t3_board.png"); PLATE = art_material("LWF_GateSign_Tier3_WelcomePlate", "t3_plate.png")
fr = Mesh("GateSignTier3"); face = Mesh("GateSignTier3_Board"); pn = Mesh("Pennants")
A = Vector((0.0, -3.2, 0))
for x in (-3.7, 3.7):
    fr.box(T(A + Vector((x, 0, 2.4))), (0.26, 0.26, 4.8), GREEN)
    fr.box(T(A + Vector((x, 0, 4.86))), (0.36, 0.36, 0.1), GOLD)
    for dx in (-1, 1):
        fr.box(T(A + Vector((x + dx * 0.32, 0, 3.55))) @ RY(dx * math.radians(45)), (0.1, 0.12, 0.7), GREEN)
fr.box(T(A + Vector((0, 0, 4.95))), (8.0, 0.22, 0.14), GREEN)
board((fr, face), T(A + Vector((0, -0.18, 4.15))), 6.8, 1.47, 0.08, GOLD, 0, ("tier3", "LetteringArea", 0.0, 0.0, 4.0, 1.15))
board((fr, face), T(A + Vector((0, -0.16, 5.45))), 2.7, 0.54, 0.05, GREEN, 1)
prng = random.Random(7)
for x in (-5.0, 5.0):
    c = A + Vector((x, -0.3, 0))
    fr.box(T(c + Vector((0, 0, 0.25))), (1.2, 0.5, 0.5), TERRA); fr.box(T(c + Vector((0, 0, 0.5))), (1.12, 0.42, 0.04), SOIL)
    for k in range(10):
        p = c + Vector((prng.uniform(-.5, .5), prng.uniform(-.17, .17), 0.56))
        fr.ball(T(p), prng.uniform(.09, .15), GRASS_D if k % 3 else (GOLD, CREAM, TERRA)[k % 3 - 0 if False else (k // 3) % 3])
pennants(pn, A + Vector((-3.55, -0.2, 3.62)), A + Vector((3.55, -0.2, 3.62)), 16, 0.22, 0.12)
objs = [fr.link([PAL]), face.link([ART, PLATE]), pn.link([PAL]), bpy.data.objects["LetteringArea"]]
export("lwf_gate_sign_tier3_v1", objs)

json.dump({"placement": "Place every asset root at Godot (0, 0, 32), no rotation. Local +Z points down the lane away from the farm.",
           "lettering_areas": AREAS}, open(os.path.join(OUT, "lettering_areas.json"), "w"), indent=1)
print("AREAS", json.dumps(AREAS))
