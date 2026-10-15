import bpy, os, json, math, hashlib, struct
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
OUT=os.path.dirname(os.path.abspath(__file__))
V1=os.path.join(os.path.dirname(OUT),'blender-v1','coffee-van.blend')
SERVER='C:/Projects/festival-tycoon/game/assets/characters/service-servers-v1/lwf_server_food_male_v1.glb'
bpy.ops.wm.open_mainfile(filepath=V1)
removed=[]
for o in list(bpy.data.objects):
 if o.name.startswith(('Open upward hatch','Sage hatch underside','Hatch support','Hatch hinge')):
  removed.append(o.name); bpy.data.objects.remove(o,do_unlink=True)
moved=[]
for o in bpy.data.objects:
 if o.name.startswith(('Menu oak frame','Menu black face','Menu heading','Menu item')):
  o.location.y-=.30; moved.append(o.name)
asset=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.startswith('STUDIO')]
bpy.ops.object.select_all(action='DESELECT')
for o in asset:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'coffee-van.glb'),export_format='GLB',use_selection=True,export_apply=True)
scene=bpy.context.scene; scene.cycles.samples=32; scene.render.threads_mode='FIXED'; scene.render.threads=8
scene.use_nodes=False
def camera(loc,target=(0,-.12,1.35),scale=6.4,w=1400,h=1000):
 cam=scene.camera; cam.location=loc; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=scale; scene.render.resolution_x=w; scene.render.resolution_y=h; scene.render.resolution_percentage=100
def render(name):
 scene.render.filepath=os.path.join(OUT,name); bpy.ops.render.render(write_still=True)
camera((-5.7,-9.5,3.4)); bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'coffee-van.blend'))
# Import the exact current game server, never included in van export.
before=set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=SERVER); server=list(set(bpy.data.objects)-before)
review=bpy.data.collections.new('REVIEW ONLY actual game food server'); scene.collection.children.link(review)
for o in server:
 for c in list(o.users_collection):c.objects.unlink(o)
 review.objects.link(o)
roots=[o for o in server if o.parent not in server]
placement=Matrix.Translation(Vector((.86,-.52,.715)))@Matrix.Rotation(math.pi,4,'Z')
for o in roots:o.matrix_world=placement@o.matrix_world
rig=next(o for o in server if o.type=='ARMATURE')
helpers={b.custom_shape for b in rig.pose.bones if b.custom_shape}
for o in helpers:
 o.hide_render=True; o.hide_set(True)
if rig.animation_data:
 for track in rig.animation_data.nla_tracks:track.mute=True
actions={a.name:a for a in bpy.data.actions}
def setclip(name,frame):
 a=next(a for n,a in actions.items() if n==name or n.startswith(name+'_') or n.endswith('/'+name) or n.endswith('_'+name))
 rig.animation_data_create(); rig.animation_data.action=a; scene.frame_set(frame); bpy.context.view_layer.update()
 return a
def worldmesh(o):
 dg=bpy.context.evaluated_depsgraph_get(); e=o.evaluated_get(dg); m=e.to_mesh(); v=[e.matrix_world@p.co for p in m.vertices]; f=[tuple(p.vertices) for p in m.polygons]; e.to_mesh_clear(); return v,f
def bounds(v):return {'min':[min(p[i] for p in v) for i in range(3)],'max':[max(p[i] for p in v) for i in range(3)]}
trees=[]
for o in asset:
 v,f=worldmesh(o);trees.append((o.name,BVHTree.FromPolygons(v,f)))
meshes=[o for o in server if o.type=='MESH' and o not in helpers]
checks=[]; mins=[]; maxs=[]; sockets=[]
socket=next((o for o in server if 'RightHand_Food' in o.name),None)
for clip in ['server_idle','server_turn','server_pickup','server_offer','server_retract','server_reset']:
 a=setclip(clip,1); end=round(a.frame_range[1]); hits={}
 for frame in range(1,end+1):
  setclip(clip,frame); allv=[]
  for o in meshes:
   v,f=worldmesh(o); allv+=v; t=BVHTree.FromPolygons(v,f)
   for name,env in trees:
    if name=='Interior floor':continue
    overlap=t.overlap(env)
    if overlap:hits[name]=hits.get(name,0)+len(overlap)
  b=bounds(allv); mins.append(b['min']);maxs.append(b['max'])
  if frame==end and socket:sockets.append({'clip':clip,'position':list(socket.matrix_world.translation)})
 checks.append({'clip':clip,'frames_checked':end,'surface_intersection_pairs_by_van_object':hits})
setclip('server_offer',13)
posed=bounds([v for o in meshes for v in worldmesh(o)[0]])
report={'source_v1':V1,'server_source_current_game':SERVER,'server_sha256':hashlib.sha256(open(SERVER,'rb').read()).hexdigest(),'removed_objects':removed,'menu_shift_m':[0,-.30,0],'menu_face_y_m':-1.008,'server_root_feet_translation_m':[.86,-.52,.715],'server_yaw_blender_deg':180,'server_scale':1,'server_offer_bounds':posed,'all_tested_frames_bounds':{'min':[min(p[i] for p in mins) for i in range(3)],'max':[max(p[i] for p in maxs) for i in range(3)]},'clips':checks,'socket_endpoints':sockets,'triangles':sum(len(o.data.polygons) for o in asset),'materials':len(set(m.name for o in asset for m in o.data.materials)),'textures_van':0,'camera_elevation_deg':math.degrees(math.atan2(58,72)),'validation_limit':'Triangle surface intersection checks do not prove volume containment, accessibility or all possible motions. Existing clips sampled every frame, no IK retargeting or game integration.'}
with open(os.path.join(OUT,'fit-review.json'),'w') as f:json.dump(report,f,indent=2)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'coffee-van-server-review.blend'))
print('FIT_REPORT',json.dumps(report),flush=True)
for o in server:o.hide_render=True
render('render-main.png')
for o in server:o.hide_render=o in helpers
render('render-main-server.png')
# Actual game elevation; close framing for an inspectable asset review.
paths=[]
for i,deg in enumerate([225,315,45,135]):
 a=math.radians(deg); target=Vector((0,0,1.35)); offset=Vector((72*math.cos(a),72*math.sin(a),58));camera(target+offset,target,6.4,700,500); name='game-angle-'+str(i+1)+'.png';render(name); paths.append(os.path.join(OUT,name))
# Stitch rendered pixels with Blender image buffers, no generated imagery.
sheet=bpy.data.images.new('Review four-angle sheet',width=1400,height=1000,alpha=True)
pixels=[0.0]*(1400*1000*4)
for i,path in enumerate(paths):
 im=bpy.data.images.load(path,check_existing=False); p=list(im.pixels); x=(i%2)*700;y=(1-i//2)*500
 for row in range(500):pixels[((y+row)*1400+x)*4:((y+row)*1400+x+700)*4]=p[row*700*4:(row+1)*700*4]
sheet.pixels=pixels;sheet.filepath_raw=os.path.join(OUT,'gameplay-four-angle-server.png');sheet.file_format='PNG';sheet.save()
print('DONE',flush=True)
