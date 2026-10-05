# Backstage v1 production: lwf_trailer_stage_v3 (chassis turned round, north-end band stairs), lwf_crowd_barrier_v1,
# the Tier 1 backstage props, and backstage_layout_v1.json (world placements).
#   blender -b --python build_backstage.py -- <out_dir>
# Godot axes in the JSON; Blender authoring with +Y = Godot -Z (glTF export_yup).
import bpy, bmesh, math, sys, os, json, hashlib
from mathutils import Vector as V, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
GAME = "C:/Projects/festival-tycoon/game/assets/environment/"
REPORT = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    ws = [o.matrix_world @ v.co for o in objs if o.type == 'MESH' for v in o.data.vertices]
    REPORT[name] = dict(file=name + ".glb", nodes=[o.name for o in objs],
                        triangles=sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons),
                        bounds_godot=[[round(min(w.x for w in ws), 3), round(min(w.z for w in ws), 3), round(min(-w.y for w in ws), 3)],
                                      [round(max(w.x for w in ws), 3), round(max(w.z for w in ws), 3), round(max(-w.y for w in ws), 3)]],
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest())
    print("EXPORTED", name, REPORT[name]["triangles"])


class Kit:
    """one bmesh per mesh; every face takes a fixed UV (a palette swatch centre)"""
    def __init__(self, name): self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def _paint(self, verts, uv):
        for f in {f for v in verts for f in v.link_faces}:
            for l in f.loops: l[self.uv].uv = uv
    def box(self, c, s, uv, rot=None):
        M = Matrix.Translation(V(c)) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*s, 1))
        self._paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=M)["verts"], uv)
    def cyl(self, c, r, depth, uv, seg=6, rot=None):
        M = Matrix.Translation(V(c)) @ (rot.to_matrix().to_4x4() if rot else Matrix())
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=depth, matrix=M)["verts"], uv)
    def tube(self, pts, r, uv, seg=6):
        for a, b in zip(pts, pts[1:]):
            a, b = V(a), V(b); d = b - a
            self.cyl((a + b) / 2, r, d.length + r * 0.6, uv, seg=seg, rot=d.to_track_quat('Z', 'Y').to_euler())
    def link(self, mat):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o


# ================================================================ 1. trailer stage v3
reset()
bpy.ops.import_scene.gltf(filepath=GAME + "lwf_trailer_stage_v2.glb")
objs = {o.name: o for o in bpy.data.objects}
bpy.data.objects.remove(objs.pop("LWF_TrailerStage_Access"))
DECK_X0, DECK_X1 = -4.82, 3.11                       # deck ends along the trailer (stage-local x)
XC = (DECK_X0 + DECK_X1) / 2
MIR = Matrix.Translation((XC, 0, 0)) @ Matrix.Diagonal((-1, 1, 1, 1)) @ Matrix.Translation((-XC, 0, 0))
for n in ("LWF_TrailerStage_Base", "LWF_TrailerStage_Rails"):
    me = objs[n].data; me.transform(objs[n].matrix_world.inverted() @ MIR @ objs[n].matrix_world); me.flip_normals()
mat = objs["LWF_TrailerStage_Base"].data.materials[0]
WOOD, WOOD_MID, WOOD_DK, STEEL, GREEN = (0.625, 0.625), (0.375, 0.625), (0.125, 0.625), (0.375, 0.875), (0.625, 0.875)
# 1.2 m opening in the (now north) end rail
SY0, SY1 = 0.02, 1.22                                 # stair width, Blender y (Godot local z -0.02 .. -1.22)
rails = objs["LWF_TrailerStage_Rails"]; bm = bmesh.new(); bm.from_mesh(rails.data)
for yc in (SY0, SY1):
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, yc, 0), plane_no=(0, 1, 0))
dead = [f for f in bm.faces if f.calc_center_median().x > 2.6 and SY0 + 0.01 < f.calc_center_median().y < SY1 - 0.01 and f.calc_center_median().z > 1.22]
bmesh.ops.delete(bm, geom=dead, context='FACES'); bm.to_mesh(rails.data); bm.free()
# band stairs off the north deck edge, centred on the end face
k = Kit("LWF_TrailerStage_BandStairs")
N_RISE, RISE, GOING = 8, 0.15, 0.31
for i in range(N_RISE - 1):
    x0 = DECK_X1 + i * GOING; h = 1.2 - (i + 1) * RISE
    k.box((x0 + GOING / 2, (SY0 + SY1) / 2, h - 0.02), (GOING + 0.02, SY1 - SY0, 0.04), WOOD if i % 2 == 0 else WOOD_MID)
XB = DECK_X1 + (N_RISE - 1) * GOING
for y in (SY0, SY1):
    k.tube([(DECK_X1, y, 1.16), (XB, y, 0.02)], 0.04, STEEL, seg=4)                 # stringers
    k.tube([(DECK_X1, y, 2.15), (XB, y, 1.05)], 0.025, WOOD_MID, seg=5)             # handrails
    for x, z0, z1 in ((DECK_X1, 1.2, 2.15), (XB, 0.0, 1.05)): k.tube([(x, y, z0), (x, y, z1)], 0.03, STEEL, seg=5)
    k.box((DECK_X1 - 0.16, y, 1.7), (0.06, 0.06, 1.0), STEEL)                        # rail posts at the opening
stairs = k.link(mat)
export([objs["LWF_TrailerStage_Base"], objs["LWF_TrailerStage_Rails"], objs["LWF_TrailerStage_Fittings"], stairs], "lwf_trailer_stage_v3")
def W(xl, yl): return [round(-16 - yl, 3), round(11 - xl, 3)]                          # stage-local Blender -> world (x, z)
REPORT["lwf_trailer_stage_v3"].update(
    placement="identical to v2: same scene node transform (world (-16, 0, 11), rotated 90 degrees)",
    changes=["chassis (Base, Rails) mirrored about stage-local x -0.855: drawbar at the south end, deck unchanged",
             "LWF_TrailerStage_Access removed", "new LWF_TrailerStage_BandStairs at the north end", "1.2 m opening in the north end rail"],
    stairs_world=dict(x=[W(0, SY1)[0], W(0, SY0)[0]], top_edge_z=W(DECK_X1, 0)[1], bottom_step_front_z=W(XB, 0)[1], risers=N_RISE, rise_m=RISE, going_m=GOING),
    drawbar_world=dict(z=[15.82, 17.66], x_at_deck=[-18.26, -14.97], x_at_hitch=[-16.83, -16.41], ground_contact=False))

# ================================================================ 2. barrier and props (shared palette)
PAL = {0: "8a9093", 1: "6b7276", 2: "232527", 3: "b9bec2", 4: "b08a5a", 5: "8d6b44", 6: "2e3a4f", 7: "9a9da2"}
def U(slot): return ((8 * slot + 4) / 64.0, 0.5)
def palette_mat():
    m = bpy.data.materials.new("LWF_Backstage_MattePalette"); m.use_nodes = True
    img = bpy.data.images.new("lwf_backstage_palette", 64, 8); px = []
    for y in range(8):
        for s in range(8):
            hx = PAL[s]; px += ([int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9; b.inputs["Specular IOR Level"].default_value = 0.25
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); return m

BAR_L = 2.3
reset(); pm = palette_mat()
k = Kit("LWF_CrowdBarrier")
r, zb, zt, t = 0.13, 0.20, 1.088, 0.036; X0 = -BAR_L / 2   # zt + t keeps the old 1.12 m top
pts = []
for cx, cz, a0 in ((BAR_L / 2 - r, zt - r, 0), (X0 + r, zt - r, 90), (X0 + r, zb + r, 180), (BAR_L / 2 - r, zb + r, 270)):
    for j in range(4):
        a = math.radians(a0 + j * 30); pts.append((cx + r * math.cos(a), 0, cz + r * math.sin(a)))
pts.append(pts[0]); k.tube(pts, t, U(0), seg=6)
NBAR = 6                                   # 5 bars of 40 mm at about 0.36 m, so each stays whole at zoom 62 (was 14 of 10 mm)
for i in range(1, NBAR):
    x = X0 + r * 0.6 + i * (BAR_L - 1.2 * r) / NBAR; k.box((x, 0, (zb + zt) / 2), (0.04, 0.04, zt - zb - 0.02), U(0))
for x in (X0 + 0.32, BAR_L / 2 - 0.32):
    for sy in (-1, 1): k.tube([(x, 0, zb + 0.02), (x + 0.02 * sy, sy * 0.30, 0.01)], 0.018, U(0), seg=5)
    k.box((x, 0, 0.012), (0.05, 0.64, 0.02), U(1))
k.tube([(BAR_L / 2, 0, 0.86), (BAR_L / 2 + 0.08, 0, 0.86), (BAR_L / 2 + 0.08, 0, 0.80)], 0.01, U(1), seg=4)   # hook (+X end)
k.tube([(X0, 0, 0.88), (X0 - 0.035, 0, 0.88)], 0.014, U(1), seg=4)                                        # eye (-X end)
bar = k.link(pm)
export([bar], "lwf_crowd_barrier_v1")
REPORT["lwf_crowd_barrier_v1"].update(length_m=BAR_L, height_m=zt, pitch_m=2.4, origin="centre bottom", length_axis="local +X (hook end)", feet_depth_m=0.64)

def flight_case(k, c, s, rot=0.0):
    R = Euler((0, 0, rot)); k.box(c, s, U(2), rot=R)
    for dz in (-s[2] / 2 + 0.015, s[2] / 2 - 0.015):
        k.box((c[0], c[1], c[2] + dz), (s[0] + 0.012, s[1] + 0.012, 0.022), U(3), rot=R)
    k.box((c[0], c[1], c[2] + s[2] * 0.15), (s[0] + 0.008, s[1] + 0.008, 0.015), U(3), rot=R)
def crate(k, c, s, rot=0.0):
    R = Euler((0, 0, rot)); k.box(c, s, U(4), rot=R)
    for dz in (-0.3, 0.0, 0.3): k.box((c[0], c[1], c[2] + dz * s[2]), (s[0] + 0.01, s[1] + 0.01, 0.03), U(5), rot=R)

PROPS = {}
reset(); pm = palette_mat(); k = Kit("LWF_FlightCaseStack")
flight_case(k, (0, 0, 0.28), (1.1, 0.6, 0.56)); flight_case(k, (0.05, 0.03, 0.74), (0.8, 0.5, 0.36), rot=0.05)
export([k.link(pm)], "lwf_backstage_flight_case_stack_v1")
reset(); pm = palette_mat(); k = Kit("LWF_FlightCaseTall")
flight_case(k, (0, 0, 0.4), (0.6, 0.6, 0.8))
export([k.link(pm)], "lwf_backstage_flight_case_tall_v1")
reset(); pm = palette_mat(); k = Kit("LWF_CratePile")
crate(k, (-0.31, 0, 0.22), (0.6, 0.45, 0.44), rot=0.2); crate(k, (0.31, 0.05, 0.22), (0.6, 0.45, 0.44), rot=-0.0)
crate(k, (0.0, 0.03, 0.62), (0.55, 0.42, 0.36), rot=0.4)
export([k.link(pm)], "lwf_backstage_crate_pile_v1")
reset(); pm = palette_mat(); k = Kit("LWF_FoldingChair")
for dx in (-0.2, 0.2):                                 # faces +Y (Godot -Z): back at -Y
    k.tube([(dx, -0.18, 0), (dx, 0.2, 0.45)], 0.012, U(7), seg=5)
    k.tube([(dx, 0.2, 0), (dx, -0.16, 0.45), (dx, -0.22, 0.85)], 0.012, U(7), seg=5)
k.box((0, 0.0, 0.45), (0.42, 0.40, 0.03), U(6)); k.box((0, -0.2, 0.7), (0.42, 0.03, 0.28), U(6), rot=Euler((-0.15, 0, 0)))
export([k.link(pm)], "lwf_folding_chair_v1")

# ================================================================ 3. layout (world metres, Godot axes; yaw = degrees about +Y)
def yaw_for(dx, dz):                                  # yaw that turns local +X onto the world direction (dx, dz)
    return round(math.degrees(math.atan2(-dz, dx)), 2)
barriers = []
X_LINE, Z_START, P = -14.0, 6.8, 2.4                  # east line: north end leaves 1.53 m to the deck's NE corner (-15.08, 7.89)
for i in range(5):
    zc = Z_START - BAR_L / 2 - i * P
    barriers.append(dict(run="east", x=X_LINE, z=round(zc, 3), yaw=yaw_for(0, -1)))
a = V((X_LINE, Z_START - 4 * P - BAR_L - 0.1)); dirv = V((-1.9, -1.35)).normalized(); b = a + dirv * BAR_L
barriers.append(dict(run="east_end_angled", x=round((a.x + b.x) / 2, 3), z=round((a.y + b.y) / 2, 3), yaw=yaw_for(dirv.x, dirv.y)))
for i in range(5):
    xc = -18.3 - BAR_L / 2 - i * P
    barriers.append(dict(run="west_closure", x=round(xc, 3), z=7.75, yaw=yaw_for(-1, 0)))
props = [dict(file="lwf_backstage_flight_case_stack_v1.glb", x=-18.6, z=3.6, yaw=11.5),
         dict(file="lwf_backstage_flight_case_tall_v1.glb", x=-19.5, z=2.2, yaw=-23.0),
         dict(file="lwf_backstage_crate_pile_v1.glb", x=-20.5, z=-1.5, yaw=17.0),
         dict(file="lwf_folding_chair_v1.glb", x=-18.5, z=-2.6, yaw=34.0),
         dict(file="lwf_folding_chair_v1.glb", x=-17.65, z=-2.2, yaw=-17.0)]
layout = dict(units="metres, Godot world axes; yaw in degrees about +Y; barrier origin = centre bottom, length along local +X",
              barrier_file="lwf_crowd_barrier_v1.glb", barrier_length_m=BAR_L, barrier_feet_depth_m=0.64,
              barriers=barriers, props=props,
              gaps=dict(stage_front=dict(start=[X_LINE, Z_START], to=[-15.08, 7.89], width_m=round(math.hypot(1.08, Z_START - 7.89), 2)),
                        house=dict(start=[round(b.x, 3), round(b.y, 3)], to_house_wall_z=-8.5, width_m=round(-8.5 - b.y, 2) * -1)),
              lines=dict(east=[[X_LINE, Z_START], [X_LINE, round(a.y + 0.1, 3)]], east_end=[[round(a.x, 3), round(a.y, 3)], [round(b.x, 3), round(b.y, 3)]],
                         west_closure=[[-18.3, 7.75], [round(-18.3 - 4 * P - BAR_L, 3), 7.75]]),
              stage=REPORT["lwf_trailer_stage_v3"]["stairs_world"], drawbar=REPORT["lwf_trailer_stage_v3"]["drawbar_world"])
json.dump(layout, open(os.path.join(OUT, "backstage_layout_v1.json"), "w"), indent=1)
json.dump(REPORT, open(os.path.join(OUT, "backstage_report.json"), "w"), indent=1)
print("DONE")
