import bpy, json, math
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
s = bpy.context.scene
s.unit_settings.system = 'METRIC'
s.unit_settings.scale_length = 1
parts = []

def color(h):
    vals = [int(h[i:i+2], 16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4 for v in vals)+(1,)

def material(name, h):
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=color(h)
    bs=m.node_tree.nodes['Principled BSDF'];bs.inputs['Base Color'].default_value=color(h);bs.inputs['Roughness'].default_value=.92
    return m

oak=material('LWF_AgedOak','795331');wood=material('LWF_WornOak','96744D')
dark=material('LWF_DarkTimber','503A27');sage=material('LWF_SageCanvas','687B59')
cream=material('LWF_CreamCanvas','D2C5A4');iron=material('LWF_DarkIron','39453F')
stone=material('LWF_StoneFeet','9F8C6B')
for m in (sage,cream):m.use_backface_culling=False

def obj(name, verts, faces, mat, group):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.materials.append(mat)
    o=bpy.data.objects.new(name,mesh);s.collection.objects.link(o);o['group']=group;parts.append(o);return o

def box(name, loc, size, mat, group='structure'):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat);o['group']=group;parts.append(o);return o

def beam(name,a,b,width,mat,group='structure'):
    a,b=Vector(a),Vector(b);o=box(name,(a+b)/2,(width,width,(b-a).length),mat,group)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o

# Short square feet and exposed posts: no cabin, trailer, wheels or tow bar.
for x in (-1.65,1.65):
    for y in (-1.02,1.02):
        box('Foot',(x,y,.055),(.29,.29,.11),stone)
        box('Post',(x,y,1.38),(.14,.14,2.65),oak)
        beam('Corner brace',(x,y,2.12),(x-.42*(1 if x>0 else -1),y,2.61),.09,dark)
for y in (-1.02,1.02):box('Eave rail',(0,y,2.65),(3.54,.15,.14),dark)
for x in (-1.65,1.65):box('Side top rail',(x,0,2.65),(.14,2.18,.14),oak)
box('Low rear rail',(0,1.02,.69),(3.42,.13,.11),oak)
for i in range(12):
    x=-1.51+i*.275
    box('Rear half-height board',(x,1.02,.55),(.265,.065,.94),wood if i%3 else oak)
# Counter at 1.05m with simple panel apron and foot rail.
box('Countertop',(0,-.95,1.045),(3.55,.65,.11),wood,'counter')
box('Counter apron',(0,-.92,.535),(3.28,.11,.91),oak,'counter')
for x in (-1.16,-.58,0,.58,1.16):box('Counter panel batten',(x,-.987,.535),(.035,.022,.83),dark,'counter')
box('Sage counter band',(0,-.99,.88),(3.18,.018,.13),sage,'counter')
for x in (-1.25,1.25):box('Counter support',(x,-.84,.52),(.13,.30,.93),dark,'counter')
beam('Bar footrail',(-1.52,-1.13,.25),(1.52,-1.13,.25),.055,iron,'counter')
box('Rear work ledge',(0,.81,1.01),(2.95,.40,.09),wood,'counter')

# Low pitched striped canvas canopy, with restrained straight valance.
for i in range(8):
    x0=-1.95+i*.4875;x1=x0+.4875;m=cream if i%4!=0 else sage
    for side in (-1,1):
        y=side*1.40
        obj('Canvas panel',[(x0,0,3.02),(x1,0,3.02),(x1,y,2.70),(x0,y,2.70)],[(0,1,2,3)],m,'canopy')
        obj('Canvas valance',[(x0,y,2.70),(x1,y,2.70),(x1,y,2.53),(x0,y,2.53)],[(0,1,2,3)],m,'canopy')
beam('Ridge pole',(-1.98,0,3.025),(1.98,0,3.025),.055,dark,'canopy')
for x in (-1.95,1.95):
    for side in (-1,1):beam('Canopy edge',(x,0,3.02),(x,side*1.40,2.70),.045,dark,'canopy')

# Painted sign face, flush-mounted rather than floating 3D text.
box('Sign frame',(0,-1.44,2.26),(1.92,.09,.45),dark,'sign')
for x in (-.75,.75):box('Sign hanger',(x,-1.44,2.52),(.035,.035,.18),iron,'sign')
image=bpy.data.images.load(str(ROOT/'drinks-sign.png'));image.pack()
signmat=bpy.data.materials.new('LWF_PaintedDrinksSign');signmat.use_nodes=True
bs=signmat.node_tree.nodes['Principled BSDF'];bs.inputs['Roughness'].default_value=.95
tex=signmat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image;signmat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
uvnode=signmat.node_tree.nodes.new('ShaderNodeUVMap');uvnode.uv_map='SignUV';signmat.node_tree.links.new(uvnode.outputs['UV'],tex.inputs['Vector'])
face=obj('Painted drinks face',[(-.90,-1.49,2.065),(.90,-1.49,2.065),(.90,-1.49,2.455),(-.90,-1.49,2.455)],[(0,1,2,3)],signmat,'sign')
uv=face.data.uv_layers.new(name='SignUV')
for li,co in zip(face.data.polygons[0].loop_indices,[(0,0),(1,0),(1,1),(0,1)]):uv.data[li].uv=co

assets=[]
for group in ('structure','counter','canopy','sign'):
    objects=[o for o in s.objects if o.get('group')==group];bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();o=bpy.context.object;o.name='LWF_DrinksStallV1_'+group.title()
    s.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR');bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);assets.append(o)
for o in assets:
    for v in o.data.vertices:v.co.x*=.78;v.co.y*=.75
bpy.ops.object.select_all(action='DESELECT')
for o in assets:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'lwf_drinks_stall_prototype_v1.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_animations=False)

counts={}
corners=[]
for o in assets:
    o.data.calc_loop_triangles();counts[o.name]=len(o.data.loop_triangles);corners += [o.matrix_world@Vector(c) for c in o.bound_box]
lo=[min(v[i] for v in corners) for i in range(3)];hi=[max(v[i] for v in corners) for i in range(3)]
meta={'status':'review only; dual approval pending','units':'metres','bounds_blender_min':lo,'bounds_blender_max':hi,
      'dimensions_wdh':[hi[0]-lo[0],hi[1]-lo[1],hi[2]-lo[2]],'triangles':sum(counts.values()),'parts_triangles':counts,
      'proposed_reserved_footprint':[3.0,2.0],'front':'Blender -Y / Godot +Z','countertop_height':1.10,
      'anchors_godot':{'service_face_center':[0,0,.95625],'customer_stop_proposal':[0,0,1.75],'staff_handoff':[0,1.10,.7125]},
      'ownership':'anchors/queue/stock/identity are integration proposals only; simulation owns them'}
(ROOT/'technical.json').write_text(json.dumps(meta,indent=2),encoding='utf-8');print(json.dumps(meta))

review=[]
def import_context(path,shift):
    before=set(s.objects);bpy.ops.import_scene.gltf(filepath=path)
    added=[o for o in s.objects if o not in before]
    roots=[o for o in added if o.parent not in added]
    for o in roots:o.matrix_world=Matrix.Translation(Vector(shift))@o.matrix_world
    for o in added:o.name='REVIEW_'+o.name
    meshes=[o for o in added if o.type=='MESH'];review.extend(meshes);return meshes

adult=import_context('C:/Projects/festival-tycoon/assets/runtime/characters/lwf_generic_attendee_v1.glb',(1,-2.0,0))
van=import_context('C:/Projects/festival-tycoon/assets/runtime/environment/lwf_food_van_chassis_v1.glb',(-7,0,0))
flap=import_context('C:/Projects/festival-tycoon/assets/runtime/environment/lwf_food_van_serving_flap_v1.glb',(-4.85,-1.17,2.25))
for o in flap:o.rotation_euler.x=math.radians(-105)
fascia=import_context('C:/Projects/festival-tycoon/assets/runtime/environment/lwf_food_van_fascia_v1.glb',(-4.85,-1.18,2.49))
ground=box('REVIEW_Ground',(-2,0,-.075),(80,70,.15),material('REVIEW_Grass','8B995E'),'review')
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
world=bpy.data.worlds.new('REVIEW_Daylight');world.color=(.34,.38,.34);s.world=world
bpy.ops.object.light_add(type='SUN',location=(0,0,8));bpy.context.object.rotation_euler=(.52,-.4,-.44);bpy.context.object.data.energy=2.1
bpy.ops.object.light_add(type='AREA',location=(0,-5,8));bpy.context.object.data.energy=1000;bpy.context.object.data.size=7
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';s.camera=cam

def render(name,target,width,yaw,context,res=(1600,1000),elev=35):
    for o in review:o.hide_render=not context
    t=Vector(target);a=math.radians(yaw);cam.location=t+Vector((20*math.sin(a),-20*math.cos(a),20*math.tan(math.radians(elev))))
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=width
    s.render.resolution_x,s.render.resolution_y=res;s.render.filepath=str(ROOT/name);bpy.ops.render.render(write_still=True)
render('01-drinks-stall-preview.png',(0,-.1,1.5),8.8,25,False)
render('02-adult-food-van-scale.png',(-2,-.1,1.4),16.5,25,True)
render('03-game-scale-32m.png',(-2,0,0),32,25,True,(1920,1080),math.degrees(math.atan2(58,72)))
bpy.ops.object.select_all(action='DESELECT')
for o in assets:o.select_set(True)
bpy.context.view_layer.objects.active=assets[0]
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'lwf_drinks_stall_prototype_v1.blend'))
