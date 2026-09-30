# blender -b --python crowd.py -- female.blend male.blend OUTPREFIX
import bpy, sys, math, random, json
from mathutils import Vector
a = sys.argv[sys.argv.index("--")+1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bodies = []
for f in a[:2]:
    with bpy.data.libraries.load(f) as (s, d): d.objects = [n for n in s.objects if n.startswith("Attendee")]
    for o in d.objects: bodies.append(o)
pal = json.load(open("C:/Projects/festival-tycoon/assets/source/characters/attendee-palette-concept-v1/palette-contract.json"))
basemat = bodies[0].data.materials[0]
img = [n for n in basemat.node_tree.nodes if n.type == 'TEX_IMAGE'][0].image
base = list(img.pixels)
def hx(h): return [((int(h[i:i+2],16)/255)) for i in (0,2,4)]
mats = []
for ci, cw in enumerate(pal["clothing_colourways"]):
    for hi, hc in enumerate(pal["hair_colours"]):
        px = base[:]
        slots = dict(cw["slots"]); slots.update(hc["slots"])
        for sl, h in slots.items():
            sl = int(sl); rgb = hx(h)
            # image pixels are linear floats for sRGB images? Blender stores display bytes /255 for byte images
            for y in range(8):
                for x in range(8*sl, 8*sl+8):
                    i = (y*96 + x)*4; px[i:i+3] = rgb
        im = img.copy(); im.pixels = px; im.pack()
        m = basemat.copy()
        [n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'][0].image = im
        mats.append(m)
sc = bpy.context.scene
random.seed(7)
pts = []
while len(pts) < 26:
    p = Vector((random.uniform(-4.5, 4.5), random.uniform(-3, 3), 0))
    if all((p-q).length > 0.85 for q in pts): pts.append(p)
for i, p in enumerate(pts):
    src = bodies[i % 2]
    o = src.copy(); o.data = src.data.copy(); sc.collection.objects.link(o)
    o.data.materials[0] = mats[(i*5 + i//2*3) % 16]
    o.location = p; o.rotation_euler.z = random.uniform(-math.pi, math.pi)
# ground
bpy.ops.mesh.primitive_plane_add(size=60)
g = bpy.context.object; gm = bpy.data.materials.new("g"); gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs[0].default_value = (0.20, 0.30, 0.10, 1); g.data.materials.append(gm)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (math.radians(45), math.radians(10), math.radians(-35)); sun.data.angle = math.radians(3)
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.8, 0.9, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32
try: sc.eevee.use_gtao = True; sc.eevee.use_soft_shadows = True; sc.eevee.shadow_cascade_size = '4096'
except: pass
sc.view_settings.view_transform = 'AgX'
cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
cd.type = 'ORTHO'
for span, name in [(12, "close"), (32, "game")]:
    cd.ortho_scale = span
    el = math.radians(38.9); az = math.radians(35)
    cam.location = Vector((-math.sin(az)*math.cos(el), math.cos(az)*math.cos(el), math.sin(el))) * 40
    cam.rotation_euler = (Vector((0, 0, 0.8)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.filepath = f"{a[2]}-{name}.png"; bpy.ops.render.render(write_still=True)
