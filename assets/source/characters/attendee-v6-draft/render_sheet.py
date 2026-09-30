# usage: blender -b file.blend --python render_sheet.py -- OBJNAME OUTPREFIX
import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
objname, out = argv[0], argv[1]
sc = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ('CAMERA','LIGHT'): bpy.data.objects.remove(o)
tgt = bpy.data.objects[objname]
H = tgt.dimensions.z
# world
w = bpy.data.worlds.get("W") or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value=(0.96,0.93,0.87,1); bg.inputs[1].default_value=0.45
def light(name, loc, energy, size, rot):
    d = bpy.data.lights.new(name,'AREA'); d.energy=energy; d.size=size
    o = bpy.data.objects.new(name,d); sc.collection.objects.link(o); o.location=loc; o.rotation_euler=rot
light("Key",(2.5,3.5,4.0),350,3,(math.radians(-40),0,math.radians(-35)))
light("Fill",(-3,2,2.5),90,4,(math.radians(-60),0,math.radians(50)))
light("Rim",(0,-4,3),200,3,(math.radians(50),0,0))
sc.render.engine='BLENDER_EEVEE'
sc.eevee.taa_render_samples=32
try: sc.eevee.use_gtao=True; sc.eevee.use_soft_shadows=True
except: pass
sc.view_settings.view_transform='AgX'
sc.view_settings.look='AgX - Base Contrast'
for o in bpy.data.objects:
    if o.name.startswith('REVIEW ground'): o.hide_render=True
gp=bpy.data.meshes.new('gp'); import bmesh; bm=bmesh.new(); bmesh.ops.create_grid(bm,x_segments=1,y_segments=1,size=30); bm.to_mesh(gp); g=bpy.data.objects.new('Ground',gp); sc.collection.objects.link(g)
gm=bpy.data.materials.new('gm'); gm.use_nodes=True; gm.node_tree.nodes['Principled BSDF'].inputs[0].default_value=(0.78,0.72,0.62,1); gp.materials.append(gm)
sc.render.film_transparent=False
cd = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C",cd); sc.collection.objects.link(cam); sc.camera=cam
def shot(name, az_deg, target_z, dist, lens, res=(900,1200), el=8):
    az=math.radians(az_deg); el=math.radians(el)
    t=Vector((0,0,target_z))
    cam.location = t + Vector((-math.sin(az)*math.cos(el), math.cos(az)*math.cos(el), math.sin(el)))*dist
    cam.rotation_euler = (t-cam.location).to_track_quat('-Z','Y').to_euler()
    cd.lens=lens
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath=f"{out}-{name}.png"; bpy.ops.render.render(write_still=True)
shot("34", 35, H*0.5, 3.4, 50)
shot("front", 0, H*0.5, 3.4, 50)
shot("side", 90, H*0.5, 3.4, 50)
shot("back", 180, H*0.5, 3.4, 50)
shot("face", 25, H*0.9, 1.3, 70, (900,900), 4)
shot("game", 35, H*0.5, 30, 50, (500,500), 39)
for nm in ("Attachment_Cup", "Attachment_Food"):
    if nm in bpy.data.objects:
        p = bpy.data.objects[nm].matrix_world.to_translation()
        for tag, az in (("contact", 40), ("contact2", 110)):
            a_ = math.radians(az); el_ = math.radians(15); t = p.copy()
            cam.location = t + Vector((-math.sin(a_)*math.cos(el_), math.cos(a_)*math.cos(el_), math.sin(el_))) * 0.9
            cam.rotation_euler = (t-cam.location).to_track_quat('-Z','Y').to_euler(); cd.lens = 60
            sc.render.resolution_x = sc.render.resolution_y = 700
            sc.render.filepath = f"{out}-{tag}.png"; bpy.ops.render.render(write_still=True)
