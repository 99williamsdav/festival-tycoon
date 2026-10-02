# Tier-1 stage sets v1: one GLB per genre, authored in the TRAILER STAGE'S LOCAL FRAME so each can be parented straight
# to the stage node with no offset.
#   python make_palette.py ; blender -b --python build_stage_sets.py -- <out_dir> [curtain_h] [strip_w]
# Stage-local frame (Godot): x along the trailer (steps at the -x end), +Z toward the audience (front edge z = +1.22,
# back rail z ~ -2.13), deck top y = 1.2; usable deck x ~ -4.66..+2.9 (beyond is the hitch).
# Performer marks: (96,150) -> (-0.25, 1.2, 0.25), (94,146) -> (1.75, 1.2, -0.75), drum mark (93,152) -> (-1.25, 1.2, -1.25).
import bpy, bmesh, math, sys, os, json
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
CURTAIN_H = float(argv[1]) if len(argv) > 1 else 1.6
STRIP_W = float(argv[2]) if len(argv) > 2 else 0.07
PALETTE = os.path.join(HERE, "tex", "stage_set_palette.png")
DECK = 1.2
NS = 16
def U(slot): return ((8 * slot + 4) / (8.0 * NS), 0.5)
WOOD, GLOWBASE, CREAM, GRILL, BLACK, PINK, SILVER, SHEET, TEAL, RAIL, HEAD = 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10
TABLE, TABLE_LEG, LAPTOP = 11, 12, 13
RUGS = {"folk": "A8432F", "indie": "7E6A9A", "electronic": "2F4A35", "metal": "1C1C1C"}

def G(x, y, z):
    """stage-local Godot coordinates -> Blender (glTF export maps Blender (x, y, z) to Godot (x, z, -y))"""
    return Vector((x, -z, y))
def T(v): return Matrix.Translation(v)
def D(x, y, z): return Matrix.Diagonal((x, y, z, 1))
def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
def palette_material():
    m = bpy.data.materials.new("LWF_StageSet_MattePalette"); m.use_nodes = True; nt = m.node_tree
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(PALETTE); tx.interpolation = 'Closest'
    b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9; nt.links.new(tx.outputs[0], b.inputs["Base Color"])
    return m
def flat_material(name, hexcol, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*lin(hexcol), 1); b.inputs["Roughness"].default_value = 0.95
    if emit:
        b.inputs["Emission Color"].default_value = (*lin(hexcol), 1); b.inputs["Emission Strength"].default_value = emit
    return m

class Mesh:
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def _paint(self, res, slot):
        for f in {f for v in res["verts"] for f in v.link_faces}:
            for l in f.loops: l[self.uv].uv = U(slot)
    def box(self, centre, size, slot):
        """centre in Blender coords, size (x, y, z) in Blender axes"""
        self._paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=T(centre) @ D(*size)), slot)
    def cyl(self, M, r1, r2, depth, slot, seg=8):
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r1, radius2=r2, depth=depth, matrix=M), slot)
    def link(self, mats):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        for m in mats: me.materials.append(m)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o

REPORT = {}
def export(name, objs, extra=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True,
                              export_animations=False, export_extras=True, export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons)
    REPORT[name] = dict(tris=tris, nodes=[o.name for o in objs], **(extra or {}))
    print(f"EXPORTED {path} tris={tris}")

# rug: its own mesh and its own untextured material, so a runtime albedo override can tint it per act
def rug(genre, cx, cz, along, across):
    m = Mesh("Rug"); m.box(G(cx, DECK + 0.0075, cz), (along, across, 0.015), 0)
    return m.link([flat_material("LWF_StageSet_Rug", RUGS[genre])]), dict(rug=dict(centre=[cx, DECK, cz], size_m=[along, across, 0.015], colour="#" + RUGS[genre]))

def standard_lamp(props, cx, cz, shade_hex="F2D9A0", shade_slot=None):
    props.cyl(T(G(cx, DECK + 0.02, cz)), 0.16, 0.16, 0.04, WOOD)
    props.cyl(T(G(cx, DECK + 0.84, cz)), 0.015, 0.015, 1.6, WOOD, seg=6)
    shade = Mesh("LampShade"); shade.cyl(T(G(cx, DECK + 1.66, cz)), 0.22, 0.13, 0.3, 0, seg=10)
    return shade.link([flat_material("LWF_StageSet_LampGlow", shade_hex, emit=1.5)])

def amp_box(props, cx, cz, w, h, d, body, grill, base=DECK):
    """an amp facing the audience (+Z local)"""
    props.box(G(cx, base + h / 2, cz), (w, d, h), body)
    props.box(G(cx, base + h / 2 - 0.04, cz + d / 2 + 0.005), (w - 0.08, 0.01, h - 0.16), grill)

def finish(name, genre_objs, extra):
    export(name, genre_objs, extra)

# ---------------------------------------------------------------- folk
reset(); PAL = palette_material(); props = Mesh("Props")
r, e = rug("folk", 0.3, -0.4, 3.6, 2.2)
shade = standard_lamp(props, 2.4, -1.85)
finish("lwf_stage_set_folk_v1", [r, props.link([PAL]), shade], dict(e, lamp=[2.4, DECK, -1.85]))
# ---------------------------------------------------------------- indie
reset(); PAL = palette_material(); props = Mesh("Props")
r, e = rug("indie", 0.3, -0.4, 3.6, 2.2)
amp_box(props, 2.5, -1.6, 0.8, 0.62, 0.3, CREAM, GRILL)
finish("lwf_stage_set_indie_v1", [r, props.link([PAL])], dict(e, amp=dict(centre=[2.5, DECK, -1.6], size_m=[0.8, 0.3, 0.62])))
# ---------------------------------------------------------------- electronic: a dark green rug and a teal-shaded lamp
reset(); PAL = palette_material(); props = Mesh("Props")
r, e = rug("electronic", 0.3, -0.4, 3.6, 2.2)
shade = standard_lamp(props, 2.4, -1.85, shade_hex="F2D9A0")
props.cyl(T(G(2.4, DECK + 1.83, -1.85)), 0.09, 0.09, 0.04, TEAL, seg=10)          # teal cap on the shade
# the electronic desk performer (role 0) stands at the front-centre mark (96,150) = stage-local (-0.25, 1.2, 0.25), facing the
# audience, 0.30 m behind the table's near edge; top at 0.98 m to meet the desk-kit hands. The table crosses the front
# walkway (fine for this genre); the steps stay clear. Mixer centred under the hands, laptop to one side.
TX, TZ, TL, TD, TH = -0.25, 0.825, 1.5, 0.55, 0.98
props.box(G(TX, DECK + TH - 0.02, TZ), (TL, TD, 0.04), TABLE)
for dx in (-TL / 2 + 0.12, TL / 2 - 0.12):
    for dz in (-TD / 2 + 0.06, TD / 2 - 0.06):
        props.box(G(TX + dx, DECK + (TH - 0.04) / 2, TZ + dz), (0.04, 0.04, TH - 0.04), TABLE_LEG)
top = DECK + TH
props.box(G(TX + 0.5, top + 0.01, TZ - 0.05), (0.36, 0.25, 0.02), LAPTOP)                     # laptop base
lid = T(G(TX + 0.5, top + 0.12, TZ + 0.09)) @ Matrix.Rotation(math.radians(15), 4, 'X')
props._paint(bmesh.ops.create_cube(props.bm, size=1.0, matrix=lid @ D(0.36, 0.02, 0.24)), LAPTOP)  # lid on the audience side
props.box(G(TX - 0.05, top + 0.035, TZ - 0.08), (0.62, 0.36, 0.07), BLACK)                    # mixing desk body
for i in range(6):                                                                           # fader caps and knobs
    props.box(G(TX - 0.28 + i * 0.09, top + 0.08, TZ - 0.02), (0.03, 0.06, 0.02), SILVER)
    props.box(G(TX - 0.28 + i * 0.09, top + 0.08, TZ - 0.17), (0.03, 0.03, 0.02), (PINK, TEAL, CREAM)[i % 3])
finish("lwf_stage_set_electronic_v1", [r, props.link([PAL]), shade],
       dict(e, lamp=[2.4, DECK, -1.85], table=dict(centre=[TX, DECK, TZ], size_m=[TL, TD, TH], top_y=round(DECK + TH, 3),
                                                    contents="laptop (lid on the audience side) and a mixing desk", performer_spot=[-0.25, 1.2, 0.25], performer_cell=[96, 150], table_cells_x=[97, 98], table_cells_z=[149, 151])))
# ---------------------------------------------------------------- metal: one small stack on a black rug that runs under it
reset(); PAL = palette_material(); props = Mesh("Props")
r, e = rug("metal", 0.65, -0.55, 4.3, 2.9)
for k in range(2):
    amp_box(props, 2.3, -1.8, 0.75, 0.7, 0.3, BLACK, GRILL, base=DECK + 0.015 + k * 0.7)
props.box(G(2.3, DECK + 0.015 + 1.4 + 0.14, -1.8), (0.75, 0.3, 0.28), HEAD)
finish("lwf_stage_set_metal_v1", [r, props.link([PAL])], dict(e, stack=dict(centre=[2.3, DECK, -1.8], size_m=[0.75, 0.3, 1.68])))
# ---------------------------------------------------------------- pop: tinsel curtain on a rail at the back rail
reset(); PAL = palette_material(); cur = Mesh("Curtain"); props = Mesh("Props")
X0, X1, CZ = -3.4, 2.6, -2.0
n = int((X1 - X0) / 0.2)
for i in range(n):
    x = X0 + 0.1 + i * 0.2
    cur.box(G(x, DECK + CURTAIN_H / 2, CZ), (STRIP_W, 0.02, CURTAIN_H), (PINK, SILVER)[i % 2])
props.box(G((X0 + X1) / 2, DECK + CURTAIN_H + 0.03, CZ), (X1 - X0 + 0.1, 0.05, 0.05), RAIL)
for x in (X0 - 0.05, X1 + 0.05):
    props.box(G(x, DECK + (CURTAIN_H + 0.05) / 2, CZ), (0.05, 0.05, CURTAIN_H + 0.05), RAIL)
finish("lwf_stage_set_pop_v1", [cur.link([PAL]), props.link([PAL])],
       dict(curtain=dict(x_range=[X0, X1], z=CZ, height_m=CURTAIN_H, strip_width_m=STRIP_W, strip_pitch_m=0.2)))
# ---------------------------------------------------------------- punk: bedsheet banner on two poles + LetteringArea
reset(); PAL = palette_material(); props = Mesh("Props"); sheet = Mesh("Banner")
BX, BZ, BW, BH, BOT = -0.1, -2.0, 5.0, 0.95, 1.45
for x in (BX - BW / 2 - 0.04, BX + BW / 2 + 0.04):
    props.box(G(x, DECK + (BOT + BH + 0.1) / 2, BZ), (0.06, 0.06, BOT + BH + 0.1), WOOD)
sheet.box(G(BX, DECK + BOT + BH / 2, BZ), (BW, 0.02, BH), SHEET)
for i in range(5):                                                             # a few sagging folds along the bottom hem
    sheet.box(G(BX - BW / 2 + 0.5 + i * 1.0, DECK + BOT + 0.02, BZ + 0.012), (0.5, 0.005, 0.04), SHEET)
objs = [props.link([PAL]), sheet.link([PAL])]
e = bpy.data.objects.new("LetteringArea", None); e.empty_display_type = 'PLAIN_AXES'
e.matrix_world = T(G(BX, DECK + BOT + BH / 2, BZ + 0.013)); e["width_m"] = 4.6; e["height_m"] = 0.8
bpy.context.scene.collection.objects.link(e); objs.append(e)
finish("lwf_stage_set_punk_v1", objs, dict(lettering_area=dict(node="LetteringArea", centre=[BX, round(DECK + BOT + BH / 2, 3), BZ + 0.013],
                                                              size_m=[4.6, 0.8], facing=[0, 0, 1], text_right=[1, 0, 0], up=[0, 1, 0]),
                                          banner=dict(size_m=[BW, 0.02, BH], bottom_above_deck_m=BOT)))
json.dump(REPORT, open(os.path.join(OUT, "stage_sets_report.json"), "w"), indent=1)
